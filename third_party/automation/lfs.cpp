// Original Yoake implementation of Aegisub 3.2.2's lfs surface.
// Specification: libaegisub/lua/modules/lfs.cpp and its module tests.
#include <libaegisub/lua/utils.h>
#include <filesystem>
#include <fstream>
#include <chrono>
namespace fs = std::filesystem;
using namespace agi::lua;
namespace {
const char *iterator_type = "yoake.lfs.dir";
fs::path path(lua_State *L, int i) { return fs::u8path(check_string(L, i)); }
template<int (*function)(lua_State*)> int fs_error(lua_State *L) {
    try { return function(L); }
    catch (const fs::filesystem_error &e) { lua_pushnil(L); lua_pushstring(L, e.what()); return 2; }
    catch (const std::exception &e) { lua_pushnil(L); lua_pushstring(L, e.what()); return 2; }
    catch (error_tag) { return lua_error(L); }
}
int currentdir(lua_State *L) { push_value(L, fs::current_path().u8string()); return 1; }
int chdir(lua_State *L) { fs::current_path(path(L, 1)); push_value(L, true); return 1; }
int mkdir(lua_State *L) { fs::create_directories(path(L, 1)); push_value(L, true); return 1; }
int rmdir(lua_State *L) { fs::remove(path(L, 1)); push_value(L, true); return 1; }
int touch(lua_State *L) {
    auto p = path(L, 1);
    if (!fs::exists(p)) { std::ofstream file(p); if (!file) throw fs::filesystem_error("touch", p, std::make_error_code(std::errc::io_error)); }
    fs::last_write_time(p, fs::file_time_type::clock::now()); push_value(L, true); return 1;
}
int attributes(lua_State *L) {
    auto p = path(L, 1); auto status = fs::status(p);
    std::string field = lua_isstring(L, 2) ? check_string(L, 2) : "";
    auto get_field = [&](const std::string &key) {
        if (key == "mode") {
            if (!fs::exists(status)) lua_pushnil(L);
            else push_value(L, fs::is_regular_file(status) ? "file" : fs::is_directory(status) ? "directory" : "other");
        }
        else if (key == "size") push_value(L, static_cast<double>(fs::file_size(p)));
        else if (key == "modification") {
            auto time = fs::last_write_time(p) - fs::file_time_type::clock::now() + std::chrono::system_clock::now();
            push_value(L, static_cast<double>(std::chrono::duration_cast<std::chrono::seconds>(time.time_since_epoch()).count()));
        }
        else error(L, "Invalid attribute name: %s", key.c_str());
    };
    if (!field.empty()) { get_field(field); return 1; }
    lua_newtable(L);
    for (const char *key : {"mode", "modification", "size"}) { get_field(key); lua_setfield(L, -2, key); }
    return 1;
}
struct iterator { fs::directory_iterator value; explicit iterator(const fs::path &p) : value(p) {} };
int next(lua_State *L) {
    auto &it = get<iterator>(L, 1, iterator_type);
    if (it.value == fs::directory_iterator()) return 0;
    push_value(L, it.value->path().filename().u8string()); ++it.value; return 1;
}
int close(lua_State *L) { get<iterator>(L, 1, iterator_type).value = fs::directory_iterator(); return 0; }
int gc(lua_State *L) { get<iterator>(L, 1, iterator_type).~iterator(); return 0; }
int dir(lua_State *L) { auto p = path(L, 1); lua_pushcfunction(L, exception_wrapper<next>); make<iterator>(L, iterator_type, p); return 2; }
}
extern "C" int luaopen_yoake_lfs(lua_State *L) {
    luaL_newmetatable(L, iterator_type);
    set_field<gc>(L, "__gc"); lua_newtable(L); set_field<next>(L, "next"); set_field<close>(L, "close"); lua_setfield(L, -2, "__index"); lua_pop(L, 1);
    lua_newtable(L);
    set_field(L, "attributes", static_cast<lua_CFunction>(fs_error<attributes>));
    set_field(L, "currentdir", static_cast<lua_CFunction>(fs_error<currentdir>));
    set_field(L, "chdir", static_cast<lua_CFunction>(fs_error<chdir>));
    set_field(L, "mkdir", static_cast<lua_CFunction>(fs_error<mkdir>));
    set_field(L, "rmdir", static_cast<lua_CFunction>(fs_error<rmdir>));
    set_field(L, "touch", static_cast<lua_CFunction>(fs_error<touch>));
    set_field<dir>(L, "dir"); lua_pushvalue(L, -1); lua_setglobal(L, "lfs"); return 1;
}
