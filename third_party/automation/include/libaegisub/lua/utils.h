// Yoake adapter for the unchanged Aegisub 3.2.2 re/unicode module sources.
// Implements their small native helper contract; see repository LICENSE.
#pragma once
#include <lua.hpp>
#include <string>
#include <stdexcept>
#include <limits>
#include <new>
#include <cstdarg>
#include <cstdio>
#include <type_traits>

namespace agi { namespace lua {
struct error_tag {};
inline int error(lua_State *L, const char *format, ...) {
    va_list args; va_start(args, format); lua_pushvfstring(L, format, args); va_end(args);
    throw error_tag();
}
inline void argcheck(lua_State *L, bool valid, int index, const char *message) {
    if (!valid) error(L, "bad argument #%d (%s)", index, message);
}
inline std::string check_string(lua_State *L, int index) {
    argcheck(L, lua_isstring(L, index), index, "string expected");
    size_t length; const char *text = lua_tolstring(L, index, &length);
    return std::string(text, length);
}
inline int check_int(lua_State *L, int index) {
    argcheck(L, lua_isnumber(L, index), index, "number expected");
    auto number = lua_tonumber(L, index);
    argcheck(L, number >= std::numeric_limits<int>::min() && number <= std::numeric_limits<int>::max(), index, "integer out of range");
    return static_cast<int>(number);
}
inline size_t check_uint(lua_State *L, int index) {
    auto number = check_int(L, index);
    argcheck(L, number >= 0, index, "non-negative number expected");
    return static_cast<size_t>(number);
}
inline void push_value(lua_State *L, bool value) { lua_pushboolean(L, value); }
inline void push_value(lua_State *L, double value) { lua_pushnumber(L, value); }
inline void push_value(lua_State *L, int value) { lua_pushinteger(L, value); }
template<class T, typename std::enable_if<std::is_integral<T>::value, int>::type = 0>
inline void push_value(lua_State *L, T value) { lua_pushinteger(L, static_cast<lua_Integer>(value)); }
inline void push_value(lua_State *L, void *value) { lua_pushlightuserdata(L, value); }
inline void push_value(lua_State *L, const char *value) { lua_pushstring(L, value); }
inline void push_value(lua_State *L, const std::string &value) { lua_pushlstring(L, value.data(), value.size()); }
inline void push_value(lua_State *L, lua_CFunction value) { lua_pushcfunction(L, value); }
template<int (*function)(lua_State*)> int exception_wrapper(lua_State *L) {
    try { return function(L); }
    catch (const std::exception &e) { lua_pushstring(L, e.what()); }
    catch (error_tag) { }
    catch (...) { lua_pushliteral(L, "Unexpected native module failure"); }
    return lua_error(L);
}
template<class T> void set_field(lua_State *L, const char *name, T value) { push_value(L, value); lua_setfield(L, -2, name); }
template<int (*function)(lua_State*)> void set_field(lua_State *L, const char *name) { lua_pushcfunction(L, exception_wrapper<function>); lua_setfield(L, -2, name); }
template<class T, class... Args> T *make(lua_State *L, const char *metatable, Args&&... args) {
    auto value = static_cast<T*>(lua_newuserdata(L, sizeof(T)));
    new(value) T(std::forward<Args>(args)...);
    luaL_getmetatable(L, metatable); lua_setmetatable(L, -2); return value;
}
template<class T> T &get(lua_State *L, int index, const char *metatable) {
    void *value = lua_touserdata(L, index);
    bool valid = value && lua_type(L, index) == LUA_TUSERDATA && lua_getmetatable(L, index);
    if (valid) { luaL_getmetatable(L, metatable); valid = lua_rawequal(L, -1, -2); lua_pop(L, 2); }
    argcheck(L, valid, index, metatable);
    return *static_cast<T*>(value);
}
} }
