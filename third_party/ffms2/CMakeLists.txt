cmake_minimum_required(VERSION 3.24)
project(YoakeFFMS2 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS OFF)

set(YOAKE_FFMS2_SOURCE_ROOT "" CACHE PATH "Pinned FFMS2 source checkout")
set(FFMPEG_ROOT "" CACHE PATH "Pinned dynamic FFmpeg vcpkg prefix")
if(NOT IS_DIRECTORY "${YOAKE_FFMS2_SOURCE_ROOT}/src/core")
    message(FATAL_ERROR
        "YOAKE_FFMS2_SOURCE_ROOT does not contain FFMS2 sources: "
        "'${YOAKE_FFMS2_SOURCE_ROOT}'"
    )
endif()
message(STATUS "Using pinned FFMS2 sources at ${YOAKE_FFMS2_SOURCE_ROOT}")

find_path(AVCODEC_INCLUDE_DIR libavcodec/avcodec.h PATHS "${FFMPEG_ROOT}/include" NO_DEFAULT_PATH REQUIRED)
foreach(component avformat avcodec swscale avutil swresample)
    string(TOUPPER "${component}" upper_component)
    find_library(${upper_component}_LIBRARY
        NAMES ${component}
        PATHS "${FFMPEG_ROOT}/lib"
        NO_DEFAULT_PATH
        REQUIRED
    )
endforeach()
# The pinned vcpkg zlib port names its release import library z.lib and its
# runtime zlib1.dll. Keep lookup restricted to Yoake's pinned FFmpeg prefix.
find_library(ZLIB_LIBRARY
    NAMES z zlib zlib1
    PATHS "${FFMPEG_ROOT}/lib"
    NO_DEFAULT_PATH
    REQUIRED
)

add_library(ffms2 SHARED
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/audiosource.cpp"
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/ffms.cpp"
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/filehandle.cpp"
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/indexing.cpp"
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/track.cpp"
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/utils.cpp"
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/videosource.cpp"
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/videoutils.cpp"
    "${YOAKE_FFMS2_SOURCE_ROOT}/src/core/zipfile.cpp"
)
target_include_directories(ffms2
    PUBLIC "${YOAKE_FFMS2_SOURCE_ROOT}/include"
    PRIVATE "${YOAKE_FFMS2_SOURCE_ROOT}" "${YOAKE_FFMS2_SOURCE_ROOT}/src" "${AVCODEC_INCLUDE_DIR}"
)
target_compile_definitions(ffms2 PRIVATE
    FFMS_EXPORTS
    _FILE_OFFSET_BITS=64
    __STDC_CONSTANT_MACROS
    _CRT_SECURE_NO_WARNINGS
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)
target_link_libraries(ffms2 PRIVATE
    "${AVFORMAT_LIBRARY}"
    "${AVCODEC_LIBRARY}"
    "${SWSCALE_LIBRARY}"
    "${AVUTIL_LIBRARY}"
    "${SWRESAMPLE_LIBRARY}"
    "${ZLIB_LIBRARY}"
    psapi uuid oleaut32 shlwapi gdi32 vfw32 secur32 ws2_32
    mfplat mfuuid strmiids ole32 user32 bcrypt crypt32
)
if(MSVC)
    target_compile_options(ffms2 PRIVATE /W3 /EHsc /utf-8)
endif()

install(TARGETS ffms2
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION bin
    ARCHIVE DESTINATION lib
)
install(FILES
    "${YOAKE_FFMS2_SOURCE_ROOT}/include/ffms.h"
    "${YOAKE_FFMS2_SOURCE_ROOT}/include/ffmscompat.h"
    DESTINATION include
)
