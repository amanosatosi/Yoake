cmake_minimum_required(VERSION 3.20)
project(yoake_automation C CXX)
set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(LUAJIT_ROOT "" CACHE PATH "Pinned LuaJIT source/build tree")
set(UPSTREAM "${CMAKE_CURRENT_SOURCE_DIR}/aegisub-3.2.2")
file(GLOB LUABINS_SOURCES CONFIGURE_DEPENDS "${UPSTREAM}/vendor/luabins/src/*.c")
add_library(yoake-automation SHARED yoake_lua.c modules.cpp lfs.cpp
  "${UPSTREAM}/libaegisub/lua/modules/lpeg.c"
  "${UPSTREAM}/libaegisub/lua/modules/re.cpp"
  "${UPSTREAM}/libaegisub/lua/modules/unicode.cpp"
  ${LUABINS_SOURCES})
target_include_directories(yoake-automation PRIVATE "${LUAJIT_ROOT}/src" "${CMAKE_CURRENT_SOURCE_DIR}/include")
# LPeg 0.10 / bundled luabins use the pre-5.1 spelling. Keep source bytes intact.
target_compile_definitions(yoake-automation PRIVATE luaL_reg=luaL_Reg)
find_package(Boost REQUIRED COMPONENTS regex locale)
find_package(ICU REQUIRED COMPONENTS uc i18n data)
target_link_libraries(yoake-automation PRIVATE Boost::regex Boost::locale ICU::uc ICU::i18n ICU::data)
if(WIN32)
  target_link_libraries(yoake-automation PRIVATE "${LUAJIT_ROOT}/src/lua51.lib" ole32)
else()
  target_link_libraries(yoake-automation PRIVATE "${LUAJIT_ROOT}/src/libluajit.a" m dl)
endif()
