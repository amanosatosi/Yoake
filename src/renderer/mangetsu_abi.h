#pragma once

#include <cstdarg>
#include <cstddef>
#include <cstdint>

namespace yoake::renderer::abi {

struct AssLibrary;
struct AssRenderer;
struct AssTrack;

struct AssImage {
    int width;
    int height;
    int stride;
    std::uint8_t *bitmap;
    std::uint32_t color;
    int destinationX;
    int destinationY;
    AssImage *next;
    int type;
};

struct AssImageRgba {
    int width;
    int height;
    int stride;
    std::uint8_t *rgba;
    int destinationX;
    int destinationY;
    int type;
    AssImageRgba *next;
};

struct RenderResult {
    AssImage *images;
    AssImageRgba *rgbaImages;
    int useRgba;
};

using LibraryInit = AssLibrary *(*)();
using LibraryDone = void (*)(AssLibrary *);
using SetMessageCallback = void (*)(AssLibrary *, void (*)(int, const char *, std::va_list, void *), void *);
using RendererInit = AssRenderer *(*)(AssLibrary *);
using RendererDone = void (*)(AssRenderer *);
using SetFontScale = void (*)(AssRenderer *, double);
using SetFonts = void (*)(AssRenderer *, const char *, const char *, int, const char *, int);
using ReadMemory = AssTrack *(*)(AssLibrary *, char *, std::size_t, const char *);
using FreeTrack = void (*)(AssTrack *);
using SetFrameSize = void (*)(AssRenderer *, int, int);
using SetStorageSize = void (*)(AssRenderer *, int, int);
using RenderFrameAuto = RenderResult (*)(AssRenderer *, AssTrack *, long long, int *);
using FreeImagesRgba = void (*)(AssImageRgba *);

struct Api {
    LibraryInit libraryInit = nullptr;
    LibraryDone libraryDone = nullptr;
    SetMessageCallback setMessageCallback = nullptr;
    RendererInit rendererInit = nullptr;
    RendererDone rendererDone = nullptr;
    SetFontScale setFontScale = nullptr;
    SetFonts setFonts = nullptr;
    ReadMemory readMemory = nullptr;
    FreeTrack freeTrack = nullptr;
    SetFrameSize setFrameSize = nullptr;
    SetStorageSize setStorageSize = nullptr;
    RenderFrameAuto renderFrameAuto = nullptr;
    FreeImagesRgba freeImagesRgba = nullptr;
};

} // namespace yoake::renderer::abi
