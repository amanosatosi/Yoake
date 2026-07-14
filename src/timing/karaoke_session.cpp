#include "timing/karaoke_session.h"

#include "app/document_context.h"
#include "media/media_session.h"

#include <QtCore/QStringList>

#include <algorithm>
#include <cmath>

namespace yoake::timing {
namespace {

struct KaraokeTag {
    int start = -1;
    int end = -1;
    QString spelling;
    qint64 durationMs = 0;
};

QString visibleText(QStringView source)
{
    QString result;
    bool inOverride = false;
    for (QChar character : source) {
        if (character == u'{') {
            inOverride = true;
        } else if (character == u'}' && inOverride) {
            inOverride = false;
        } else if (!inOverride) {
            result += character;
        }
    }
    return result;
}

int visibleCharacters(QStringView source)
{
    return visibleText(source).toUcs4().size();
}

QVector<KaraokeTag> karaokeTags(QStringView block)
{
    QVector<KaraokeTag> result;
    int parentheses = 0;
    for (int index = 0; index < block.size(); ++index) {
        const QChar character = block[index];
        if (character == u'(') {
            ++parentheses;
            continue;
        }
        if (character == u')') {
            parentheses = std::max(0, parentheses - 1);
            continue;
        }
        if (character != u'\\' || parentheses != 0 || index + 1 >= block.size())
            continue;

        int cursor = index + 1;
        QString spelling;
        if (block[cursor] == u'K') {
            spelling = QStringLiteral("\\K");
            ++cursor;
        } else if (block[cursor] == u'k') {
            ++cursor;
            if (cursor < block.size() && (block[cursor] == u'f' || block[cursor] == u'o')) {
                spelling = QStringLiteral("\\k") + block[cursor];
                ++cursor;
            } else {
                spelling = QStringLiteral("\\k");
            }
        } else {
            continue;
        }

        const int numberStart = cursor;
        while (cursor < block.size() && block[cursor].isDigit())
            ++cursor;
        if (numberStart == cursor)
            continue;
        bool ok = false;
        const qint64 centiseconds = block.mid(numberStart, cursor - numberStart).toLongLong(&ok);
        if (ok)
            result.push_back({index, cursor, spelling, centiseconds * 10});
        index = cursor - 1;
    }
    return result;
}

void appendOverride(QString &source, QStringView contents)
{
    if (!contents.isEmpty()) {
        source += u'{';
        source += contents;
        source += u'}';
    }
}

QVector<KaraokeSession::Slot> parseSlots(const QString &text, bool *hadKaraoke)
{
    QVector<KaraokeSession::Slot> parsedSlots;
    KaraokeSession::Slot current;
    *hadKaraoke = false;

    const auto pushCurrent = [&] {
        current.label = visibleText(current.source).replace(QStringLiteral("\\N"), QStringLiteral("↵"));
        parsedSlots.push_back(current);
        current = {};
    };

    int cursor = 0;
    while (cursor < text.size()) {
        const int open = text.indexOf(u'{', cursor);
        if (open < 0) {
            current.source += QStringView(text).mid(cursor);
            break;
        }
        current.source += QStringView(text).mid(cursor, open - cursor);
        const int close = text.indexOf(u'}', open + 1);
        if (close < 0) {
            current.source += QStringView(text).mid(open);
            break;
        }

        const QStringView block = QStringView(text).mid(open + 1, close - open - 1);
        const QVector<KaraokeTag> tags = karaokeTags(block);
        if (tags.isEmpty()) {
            current.source += QStringView(text).mid(open, close - open + 1);
        } else {
            *hadKaraoke = true;
            int blockCursor = 0;
            for (const KaraokeTag &tag : tags) {
                appendOverride(current.source, block.mid(blockCursor, tag.start - blockCursor));
                if (current.sourceDurationMs > 0 || visibleCharacters(current.source) > 0)
                    pushCurrent();
                current.tagType = tag.spelling;
                current.sourceDurationMs = tag.durationMs;
                blockCursor = tag.end;
            }
            appendOverride(current.source, block.mid(blockCursor));
        }
        cursor = close + 1;
    }
    pushCurrent();
    return parsedSlots;
}

QVector<KaraokeSession::Slot> splitAtSpaces(const KaraokeSession::Slot &slot)
{
    QVector<KaraokeSession::Slot> result;
    KaraokeSession::Slot current;
    current.tagType = slot.tagType;
    bool inOverride = false;
    for (QChar character : slot.source) {
        current.source += character;
        if (character == u'{')
            inOverride = true;
        else if (character == u'}' && inOverride)
            inOverride = false;
        else if (character == u' ' && !inOverride) {
            current.label = visibleText(current.source).replace(QStringLiteral("\\N"), QStringLiteral("↵"));
            result.push_back(current);
            current = {};
            current.tagType = slot.tagType;
        }
    }
    current.label = visibleText(current.source).replace(QStringLiteral("\\N"), QStringLiteral("↵"));
    result.push_back(current);
    return result;
}

bool isSupportedTag(const QString &tag)
{
    return tag == QStringLiteral("\\k") || tag == QStringLiteral("\\K")
        || tag == QStringLiteral("\\kf") || tag == QStringLiteral("\\ko");
}

} // namespace

KaraokeSession::KaraokeSession(app::DocumentContext *context)
    : QAbstractListModel(context), m_context(context)
{
    connect(context->lines(), &models::SubtitleModel::activeRowChanged, this, [this] {
        if (m_active && !m_committing)
            reload();
    });
}

int KaraokeSession::rowCount(const QModelIndex &parent) const
{
    return parent.isValid() ? 0 : static_cast<int>(m_slots.size());
}

QVariant KaraokeSession::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() < 0 || index.row() >= m_slots.size())
        return {};
    const Slot &slot = m_slots[index.row()];
    switch (role) {
    case LabelRole: return slot.label;
    case StartMsRole: return slot.startMs;
    case EndMsRole: return slot.endMs;
    case TagTypeRole: return slot.tagType;
    case SelectedRole: return index.row() == m_selectedIndex;
    default: return {};
    }
}

QHash<int, QByteArray> KaraokeSession::roleNames() const
{
    return {{LabelRole, "syllableLabel"}, {StartMsRole, "syllableStartMs"},
        {EndMsRole, "syllableEndMs"}, {TagTypeRole, "syllableTagType"},
        {SelectedRole, "syllableSelected"}};
}

void KaraokeSession::setTagType(const QString &tagType)
{
    if (!isSupportedTag(tagType) || m_tagType == tagType)
        return;
    m_tagType = tagType;
    for (Slot &slot : m_slots)
        slot.tagType = tagType;
    if (!m_slots.isEmpty())
        emit dataChanged(index(0), index(m_slots.size() - 1), {TagTypeRole});
    emit tagTypeChanged();
    setDirty(true);
}

void KaraokeSession::setSelectedIndex(int selectedIndex)
{
    if (m_slots.isEmpty())
        selectedIndex = -1;
    else
        selectedIndex = qBound(0, selectedIndex, static_cast<int>(m_slots.size()) - 1);
    if (m_selectedIndex == selectedIndex)
        return;
    const int previous = m_selectedIndex;
    m_selectedIndex = selectedIndex;
    if (previous >= 0)
        emit dataChanged(index(previous), index(previous), {SelectedRole});
    if (m_selectedIndex >= 0)
        emit dataChanged(index(m_selectedIndex), index(m_selectedIndex), {SelectedRole});
    emit selectedIndexChanged();
}

void KaraokeSession::beginOriginal()
{
    if (!m_active) {
        m_active = true;
        emit activeChanged();
    }
    reload();
}

void KaraokeSession::cancel()
{
    if (!m_active)
        return;
    beginResetModel();
    m_slots.clear();
    endResetModel();
    m_active = false;
    m_selectedIndex = -1;
    setDirty(false);
    emit countChanged();
    emit selectedIndexChanged();
    emit activeChanged();
    emit boundariesChanged();
}

void KaraokeSession::commit()
{
    if (!m_active || m_slots.isEmpty())
        return;
    const QString output = serializedText();
    m_committing = true;
    m_context->commitKaraokeText(output);
    m_committing = false;
    reload();
}

qint64 KaraokeSession::boundaryMs(int boundaryIndex) const
{
    return boundaryIndex >= 0 && boundaryIndex + 1 < m_slots.size()
        ? m_slots[boundaryIndex].endMs : -1;
}

qint64 KaraokeSession::slotStartMs(int index) const
{
    return index >= 0 && index < m_slots.size() ? m_slots[index].startMs : 0;
}

qint64 KaraokeSession::slotEndMs(int index) const
{
    return index >= 0 && index < m_slots.size() ? m_slots[index].endMs : 0;
}

int KaraokeSession::nearestBoundary(qint64 timeMs, qint64 toleranceMs) const
{
    int best = -1;
    qint64 bestDistance = toleranceMs + 1;
    for (int boundary = 0; boundary + 1 < m_slots.size(); ++boundary) {
        const qint64 distance = std::abs(m_slots[boundary].endMs - timeMs);
        if (distance <= toleranceMs && distance < bestDistance) {
            best = boundary;
            bestDistance = distance;
        }
    }
    return best;
}

void KaraokeSession::moveBoundary(int boundaryIndex, qint64 timeMs)
{
    if (boundaryIndex < 0 || boundaryIndex + 1 >= m_slots.size())
        return;
    const qint64 snapped = ((timeMs + 5) / 10) * 10;
    const qint64 value = std::clamp(snapped,
        m_slots[boundaryIndex].startMs, m_slots[boundaryIndex + 1].endMs);
    if (m_slots[boundaryIndex].endMs == value)
        return;
    m_slots[boundaryIndex].endMs = value;
    m_slots[boundaryIndex + 1].startMs = value;
    emit dataChanged(index(boundaryIndex), index(boundaryIndex + 1), {StartMsRole, EndMsRole});
    emit boundariesChanged();
    setDirty(true);
}

void KaraokeSession::selectAtTime(qint64 timeMs)
{
    for (int slot = 0; slot < m_slots.size(); ++slot) {
        if (timeMs >= m_slots[slot].startMs && timeMs <= m_slots[slot].endMs) {
            setSelectedIndex(slot);
            return;
        }
    }
}

void KaraokeSession::playSelected()
{
    if (m_selectedIndex < 0 || m_selectedIndex >= m_slots.size())
        return;
    m_context->media()->playRange(
        m_slots[m_selectedIndex].startMs, m_slots[m_selectedIndex].endMs);
}

void KaraokeSession::reload()
{
    const int previousSelection = m_selectedIndex;
    m_originalText = m_context->activeText();
    bool hadKaraoke = false;
    QVector<Slot> parsedSlots = parseSlots(m_originalText, &hadKaraoke);
    if (!hadKaraoke && parsedSlots.size() == 1)
        parsedSlots = splitAtSpaces(parsedSlots.front());

    const qint64 lineStart = m_context->activeStartMs();
    const qint64 lineEnd = std::max(lineStart, m_context->activeEndMs());
    if (hadKaraoke) {
        qint64 cursor = lineStart;
        for (Slot &slot : parsedSlots) {
            slot.startMs = cursor;
            slot.endMs = std::min(lineEnd, cursor + std::max<qint64>(0, slot.sourceDurationMs));
            cursor = slot.endMs;
        }
        if (!parsedSlots.isEmpty())
            parsedSlots.back().endMs = lineEnd;
    } else {
        int totalCharacters = 0;
        for (const Slot &slot : parsedSlots)
            totalCharacters += std::max(1, visibleCharacters(slot.source));
        int consumedCharacters = 0;
        for (Slot &slot : parsedSlots) {
            slot.startMs = lineStart + (lineEnd - lineStart) * consumedCharacters / std::max(1, totalCharacters);
            consumedCharacters += std::max(1, visibleCharacters(slot.source));
            slot.endMs = lineStart + (lineEnd - lineStart) * consumedCharacters / std::max(1, totalCharacters);
        }
    }

    beginResetModel();
    m_slots = std::move(parsedSlots);
    endResetModel();
    if (!m_slots.isEmpty()) {
        m_tagType = isSupportedTag(m_slots.front().tagType)
            ? m_slots.front().tagType : QStringLiteral("\\k");
        m_selectedIndex = qBound(0, previousSelection, static_cast<int>(m_slots.size()) - 1);
    } else {
        m_tagType = QStringLiteral("\\k");
        m_selectedIndex = -1;
    }
    setDirty(!hadKaraoke && m_slots.size() > 1);
    emit countChanged();
    emit tagTypeChanged();
    emit selectedIndexChanged();
    emit boundariesChanged();
}

void KaraokeSession::setDirty(bool dirty)
{
    if (m_dirty == dirty)
        return;
    m_dirty = dirty;
    emit dirtyChanged();
}

QString KaraokeSession::serializedText() const
{
    QString result;
    const qint64 lineStart = m_context->activeStartMs();
    qint64 previousCentisecond = 0;
    for (const Slot &slot : m_slots) {
        const qint64 endCentisecond = std::max(previousCentisecond,
            static_cast<qint64>(std::llround((slot.endMs - lineStart) / 10.0)));
        result += u'{';
        result += slot.tagType;
        result += QString::number(endCentisecond - previousCentisecond);
        result += u'}';
        result += slot.source;
        previousCentisecond = endCentisecond;
    }
    return result;
}

} // namespace yoake::timing
