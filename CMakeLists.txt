cmake_minimum_required(VERSION 3.24)

project(Yoake VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)

find_package(Qt6 6.8 REQUIRED COMPONENTS
    Core
    Gui
    Qml
    Quick
    QuickControls2
    Concurrent
    Multimedia
)

set(FFMS2_ROOT "" CACHE PATH "FFMS2 install prefix")
find_path(FFMS2_INCLUDE_DIR
    NAMES ffms.h
    HINTS "${FFMS2_ROOT}"
    PATH_SUFFIXES include
    REQUIRED
)
find_library(FFMS2_LIBRARY
    NAMES ffms2
    HINTS "${FFMS2_ROOT}"
    PATH_SUFFIXES lib
    REQUIRED
)
find_file(FFMS2_RUNTIME
    NAMES ffms2.dll
    HINTS "${FFMS2_ROOT}"
    PATH_SUFFIXES bin
    REQUIRED
)
add_library(FFMS2::FFMS2 SHARED IMPORTED)
get_filename_component(FFMS2_RUNTIME_DIR "${FFMS2_RUNTIME}" DIRECTORY)
set_target_properties(FFMS2::FFMS2 PROPERTIES
    IMPORTED_IMPLIB "${FFMS2_LIBRARY}"
    IMPORTED_LOCATION "${FFMS2_RUNTIME}"
    INTERFACE_INCLUDE_DIRECTORIES "${FFMS2_INCLUDE_DIR}"
)

set(FFMPEG_ROOT "" CACHE PATH "Pinned FFmpeg install prefix")
find_path(FFMPEG_INCLUDE_DIR
    NAMES libswresample/swresample.h
    PATHS "${FFMPEG_ROOT}/include"
    NO_DEFAULT_PATH
    REQUIRED
)
find_library(FFMPEG_SWRESAMPLE_LIBRARY
    NAMES swresample
    PATHS "${FFMPEG_ROOT}/lib"
    NO_DEFAULT_PATH
    REQUIRED
)
find_library(FFMPEG_AVUTIL_LIBRARY
    NAMES avutil
    PATHS "${FFMPEG_ROOT}/lib"
    NO_DEFAULT_PATH
    REQUIRED
)
find_file(FFMPEG_SWRESAMPLE_RUNTIME
    NAMES swresample-5.dll
    PATHS "${FFMPEG_ROOT}/bin"
    NO_DEFAULT_PATH
    REQUIRED
)
find_file(FFMPEG_AVUTIL_RUNTIME
    NAMES avutil-59.dll
    PATHS "${FFMPEG_ROOT}/bin"
    NO_DEFAULT_PATH
    REQUIRED
)
get_filename_component(FFMPEG_RUNTIME_DIR "${FFMPEG_SWRESAMPLE_RUNTIME}" DIRECTORY)
add_library(FFmpeg::swresample SHARED IMPORTED)
set_target_properties(FFmpeg::swresample PROPERTIES
    IMPORTED_IMPLIB "${FFMPEG_SWRESAMPLE_LIBRARY}"
    IMPORTED_LOCATION "${FFMPEG_SWRESAMPLE_RUNTIME}"
    INTERFACE_INCLUDE_DIRECTORIES "${FFMPEG_INCLUDE_DIR}"
)
add_library(FFmpeg::avutil SHARED IMPORTED)
set_target_properties(FFmpeg::avutil PROPERTIES
    IMPORTED_IMPLIB "${FFMPEG_AVUTIL_LIBRARY}"
    IMPORTED_LOCATION "${FFMPEG_AVUTIL_RUNTIME}"
    INTERFACE_INCLUDE_DIRECTORIES "${FFMPEG_INCLUDE_DIR}"
)

qt_standard_project_setup(REQUIRES 6.8)

set(YOAKE_SOURCES
    src/main.cpp
    src/app/document_context.cpp
    src/app/document_manager.cpp
    src/app/theme_manager.cpp
    src/ass/ass_document.cpp
    src/ass/ass_event_clipboard.cpp
    src/ass/ass_event_edits.cpp
    src/ass/ass_text_boundaries.cpp
    src/ass/visual_tags.cpp
    src/ass/vector_path.cpp
    src/media/ffms_audio_worker.cpp
    src/media/ffms_spectrum_worker.cpp
    src/media/ffms_video_worker.cpp
    src/media/frame_time_map.cpp
    src/media/media_session.cpp
    src/media/spectrum_analyzer.cpp
    src/media/waveform_model.cpp
    src/models/subtitle_model.cpp
    src/renderer/mangetsu_library.cpp
    src/renderer/mangetsu_session.cpp
    src/timing/karaoke_session.cpp
    src/ui/ass_highlighter.cpp
    src/ui/subtitle_overlay_item.cpp
    src/ui/spectrum_item.cpp
    src/ui/video_frame_item.cpp
    src/ui/video_viewport.cpp
    src/ui/visual_snap_service.cpp
    src/ui/visual_tool_base.cpp
    src/ui/builtin_visual_tools.cpp
    src/ui/vector_path_visual_tools.cpp
    src/ui/geometry_shift_visual_tool.cpp
    src/ui/visual_tool_manager.cpp
)

set(YOAKE_HEADERS
    src/app/document_context.h
    src/app/document_manager.h
    src/app/theme_manager.h
    src/ass/ass_document.h
    src/ass/ass_event.h
    src/ass/ass_event_clipboard.h
    src/ass/ass_event_edits.h
    src/ass/ass_text_boundaries.h
    src/media/ffms_audio_worker.h
    src/media/ffms_spectrum_worker.h
    src/media/ffms_video_worker.h
    src/media/frame_time_map.h
    src/media/media_session.h
    src/media/spectrum_analyzer.h
    src/media/waveform_model.h
    src/models/subtitle_model.h
    src/renderer/mangetsu_abi.h
    src/renderer/mangetsu_library.h
    src/renderer/mangetsu_session.h
    src/timing/karaoke_session.h
    src/ui/ass_highlighter.h
    src/ui/subtitle_overlay_item.h
    src/ui/spectrum_item.h
    src/ui/video_frame_item.h
    src/ui/video_viewport.h
    src/ui/visual_tool_manager.h
    src/ui/visual_tool_base.h
    src/ui/builtin_visual_tools.h
    src/ui/vector_path_visual_tools.h
    src/ui/geometry_shift_visual_tool.h
    src/ui/visual_snap_service.h
    src/ui/visual_tool.h
    src/ass/visual_tags.h
    src/ass/vector_path.h
)

qt_add_executable(yoake WIN32 ${YOAKE_SOURCES} ${YOAKE_HEADERS})

qt_add_qml_module(yoake
    URI Yoake
    VERSION 1.0
    QML_FILES
        qml/Main.qml
        qml/AssEditor.qml
        qml/AudioPane.qml
        qml/DocumentPane.qml
        qml/FindReplaceBar.qml
        qml/SubtitleGrid.qml
        qml/VideoPane.qml
)

qt_add_resources(yoake yoake_themes
    PREFIX "/yoake/themes"
    BASE "resources/themes"
    FILES
        resources/themes/yoake-dawn.json
        resources/themes/yoake-dusk.json
        resources/themes/catppuccin-mocha.json
)

target_include_directories(yoake PRIVATE src)
target_link_libraries(yoake PRIVATE
    Qt6::Core
    Qt6::Gui
    Qt6::Qml
    Qt6::Quick
    Qt6::QuickControls2
    Qt6::Concurrent
    Qt6::Multimedia
    FFMS2::FFMS2
    FFmpeg::swresample
    FFmpeg::avutil
)

if(MSVC)
    target_compile_options(yoake PRIVATE /W4 /permissive- /EHsc)
    target_compile_definitions(yoake PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
else()
    target_compile_options(yoake PRIVATE -Wall -Wextra -Wpedantic)
endif()

include(CTest)
if(BUILD_TESTING)
    find_package(Qt6 6.8 REQUIRED COMPONENTS Test)
    set(YOAKE_CORE_TEST_SUPPORT_SOURCES
        src/ass/ass_document.cpp
        src/ass/ass_event_clipboard.cpp
        src/ass/ass_event_edits.cpp
        src/ass/ass_text_boundaries.cpp
        src/ass/visual_tags.cpp
        src/ass/vector_path.cpp
        src/media/frame_time_map.cpp
        src/ui/video_viewport.cpp
        src/ui/visual_snap_service.cpp
        src/media/spectrum_analyzer.cpp
    )
    qt_add_executable(yoake_core_tests
        tests/ass_document_test.cpp
        ${YOAKE_CORE_TEST_SUPPORT_SOURCES}
    )
    target_include_directories(yoake_core_tests PRIVATE src)
    target_link_libraries(yoake_core_tests PRIVATE Qt6::Core Qt6::Test)
    add_test(NAME yoake_core_tests COMMAND yoake_core_tests)

    qt_add_executable(yoake_visual_tools_tests
        tests/visual_tools_test.cpp
        ${YOAKE_CORE_TEST_SUPPORT_SOURCES}
    )
    target_include_directories(yoake_visual_tools_tests PRIVATE src)
    target_link_libraries(yoake_visual_tools_tests PRIVATE Qt6::Core Qt6::Test)
    add_test(NAME yoake_visual_tools_tests COMMAND yoake_visual_tools_tests)

    qt_add_executable(yoake_editing_tests
        tests/document_editing_test.cpp
        src/app/document_context.cpp
        src/ui/video_viewport.cpp
        src/ui/visual_snap_service.cpp
        src/ui/visual_tool_base.cpp
        src/ui/builtin_visual_tools.cpp
        src/ui/vector_path_visual_tools.cpp
        src/ui/geometry_shift_visual_tool.cpp
        src/ui/visual_tool_manager.cpp
        src/ass/ass_document.cpp
        src/ass/visual_tags.cpp
        src/ass/vector_path.cpp
        src/ass/ass_event_clipboard.cpp
        src/ass/ass_event_edits.cpp
        src/media/ffms_audio_worker.cpp
        src/media/ffms_spectrum_worker.cpp
        src/media/ffms_video_worker.cpp
        src/media/frame_time_map.cpp
        src/media/media_session.cpp
        src/media/spectrum_analyzer.cpp
        src/media/waveform_model.cpp
        src/models/subtitle_model.cpp
        src/renderer/mangetsu_library.cpp
        src/renderer/mangetsu_session.cpp
        src/timing/karaoke_session.cpp
        src/app/document_context.h
        src/ui/video_viewport.h
        src/ui/visual_snap_service.h
        src/ui/visual_tool_manager.h
        src/ui/visual_tool_base.h
        src/ui/builtin_visual_tools.h
        src/ui/vector_path_visual_tools.h
        src/ui/geometry_shift_visual_tool.h
        src/ui/visual_tool.h
        src/ass/ass_document.h
        src/ass/ass_event.h
        src/ass/visual_tags.h
        src/ass/ass_event_clipboard.h
        src/ass/ass_event_edits.h
        src/media/ffms_audio_worker.h
        src/media/ffms_spectrum_worker.h
        src/media/ffms_video_worker.h
        src/media/frame_time_map.h
        src/media/media_session.h
        src/media/spectrum_analyzer.h
        src/media/waveform_model.h
        src/models/subtitle_model.h
        src/renderer/mangetsu_abi.h
        src/renderer/mangetsu_library.h
        src/renderer/mangetsu_session.h
        src/timing/karaoke_session.h
    )
    target_include_directories(yoake_editing_tests PRIVATE src)
    target_link_libraries(yoake_editing_tests PRIVATE
        Qt6::Core Qt6::Gui Qt6::Qml Qt6::Quick Qt6::QuickControls2
        Qt6::Concurrent Qt6::Multimedia Qt6::Test
        FFMS2::FFMS2 FFmpeg::swresample FFmpeg::avutil
    )
    if(MSVC)
        target_compile_options(yoake_editing_tests PRIVATE /W4 /permissive- /EHsc)
        target_compile_definitions(yoake_editing_tests PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
    else()
        target_compile_options(yoake_editing_tests PRIVATE -Wall -Wextra -Wpedantic)
    endif()
    add_test(NAME yoake_editing_tests COMMAND yoake_editing_tests)
    set_tests_properties(yoake_editing_tests PROPERTIES ENVIRONMENT_MODIFICATION
        "PATH=path_list_prepend:${FFMS2_RUNTIME_DIR};PATH=path_list_prepend:${FFMPEG_RUNTIME_DIR}"
    )

endif()
