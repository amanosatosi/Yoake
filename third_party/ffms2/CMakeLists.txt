cmake_minimum_required(VERSION 3.24)
project(YoakeFFMS2 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS OFF)

set(FFMS2_SOURCE_DIR "" CACHE PATH "Pinned FFMS2 source checkout")
set(FFMPEG_ROOT "" CACHE PATH "Pinned dynamic FFmpeg vcpkg prefix")
if(NOT IS_DIRECTORY "${FFMS2_SOURCE_DIR}/src/core")
    message(FATAL_ERROR "FFMS2_SOURCE_DIR does not contain FFMS2 sources")
endif()

find_path(AVCODEC_INCLUDE_DIR libavcodec/avcodec.h PATHS "${FFMPEG_ROOT}/include" NO_DEFAULT_PATH REQUIRED)
foreach(component avformat avcodec swscale avutil swresample zlib)
    string(TOUPPER "${component}" upper_component)
    find_library(${upper_component}_LIBRARY
        NAMES ${component}
        PATHS "${FFMPEG_ROOT}/lib"
        NO_DEFAULT_PATH
        REQUIRED
    )
endforeach()

add_library(ffms2 SHARED
    "${FFMS2_SOURCE_DIR}/src/core/audiosource.cpp"
    "${FFMS2_SOURCE_DIR}/src/core/ffms.cpp"
    "${FFMS2_SOURCE_DIR}/src/core/filehandle.cpp"
    "${FFMS2_SOURCE_DIR}/src/core/indexing.cpp"
    "${FFMS2_SOURCE_DIR}/src/core/track.cpp"
    "${FFMS2_SOURCE_DIR}/src/core/utils.cpp"
    "${FFMS2_SOURCE_DIR}/src/core/videosource.cpp"
    "${FFMS2_SOURCE_DIR}/src/core/videoutils.cpp"
    "${FFMS2_SOURCE_DIR}/src/core/zipfile.cpp"
)
target_include_directories(ffms2
    PUBLIC "${FFMS2_SOURCE_DIR}/include"
    PRIVATE "${FFMS2_SOURCE_DIR}" "${FFMS2_SOURCE_DIR}/src" "${AVCODEC_INCLUDE_DIR}"
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
    "${FFMS2_SOURCE_DIR}/include/ffms.h"
    "${FFMS2_SOURCE_DIR}/include/ffmscompat.h"
    DESTINATION include
)
