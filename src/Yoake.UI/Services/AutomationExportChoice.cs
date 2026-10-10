using Yoake.Core.Automation;

namespace Yoake.UI.Services;

public sealed record AutomationExportOption(AutomationFilterBinding Binding, IReadOnlyList<AutomationLine> Controls, string? Error);
public sealed record AutomationExportChoice(IReadOnlyList<AutomationFilterSettings> Filters, string Encoding);
