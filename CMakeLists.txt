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

qt_standard_project_setup(REQUIRES 6.8)

set(YOAKE_SOURCES
    src/main.cpp
    src/app/document_context.cpp
    src/app/document_manager.cpp
    src/app/theme_manager.cpp
    src/ass/ass_document.cpp
    src/ass/ass_text_boundaries.cpp
    src/media/media_session.cpp
    src/media/waveform_model.cpp
    src/media/waveform_worker.cpp
    src/models/subtitle_model.cpp
    src/renderer/mangetsu_library.cpp
    src/renderer/mangetsu_session.cpp
    src/timing/karaoke_session.cpp
    src/ui/ass_highlighter.cpp
    src/ui/subtitle_overlay_item.cpp
)

set(YOAKE_HEADERS
    src/app/document_context.h
    src/app/document_manager.h
    src/app/theme_manager.h
    src/ass/ass_document.h
    src/ass/ass_event.h
    src/ass/ass_text_boundaries.h
    src/media/media_session.h
    src/media/waveform_model.h
    src/media/waveform_worker.h
    src/models/subtitle_model.h
    src/renderer/mangetsu_abi.h
    src/renderer/mangetsu_library.h
    src/renderer/mangetsu_session.h
    src/timing/karaoke_session.h
    src/ui/ass_highlighter.h
    src/ui/subtitle_overlay_item.h
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
    qt_add_executable(yoake_core_tests
        tests/ass_document_test.cpp
        src/ass/ass_document.cpp
        src/ass/ass_text_boundaries.cpp
    )
    target_include_directories(yoake_core_tests PRIVATE src)
    target_link_libraries(yoake_core_tests PRIVATE Qt6::Core Qt6::Test)
    add_test(NAME yoake_core_tests COMMAND yoake_core_tests)
endif()
