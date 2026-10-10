#include <libaegisub/lua/utils.h>
#include <boost/locale/generator.hpp>
#include <mutex>

extern "C" int luaopen_re_impl(lua_State *L);
extern "C" int luaopen_unicode_impl(lua_State *L);
extern "C" int luaopen_lpeg(lua_State *L);
extern "C" int luaopen_luabins(lua_State *L);
extern "C" int luaopen_yoake_lfs(lua_State *L);

extern "C" int ya_preload_modules(lua_State *L) {
    static std::once_flag locale;
    std::call_once(locale, [] { std::locale::global(boost::locale::generator().generate("en_US.UTF-8")); });
    lua_getglobal(L, "package"); lua_getfield(L, -1, "preload");
    lua_pushcfunction(L, agi::lua::exception_wrapper<luaopen_re_impl>); lua_setfield(L, -2, "aegisub.__re_impl");
    lua_pushcfunction(L, agi::lua::exception_wrapper<luaopen_unicode_impl>); lua_setfield(L, -2, "aegisub.__unicode_impl");
    lua_pushcfunction(L, luaopen_lpeg); lua_setfield(L, -2, "lpeg");
    lua_pushcfunction(L, luaopen_luabins); lua_setfield(L, -2, "luabins");
    lua_pushcfunction(L, agi::lua::exception_wrapper<luaopen_yoake_lfs>); lua_setfield(L, -2, "lfs");
    lua_pop(L, 2); return 0;
}

extern "C" int ya_safe_preload_modules(lua_State *L) {
    return agi::lua::exception_wrapper<ya_preload_modules>(L);
}
