/* Yoake's native Lua error boundary. See the repository LICENSE. */
#include <stdlib.h>
#include <string.h>
#include <lua.h>
#include <lauxlib.h>
#include <lualib.h>
#include <luajit.h>
#ifdef _WIN32
#include <windows.h>
#include <objbase.h>
#define API __declspec(dllexport)
#else
#include <stdatomic.h>
#define API __attribute__((visibility("default")))
#endif

typedef char *(*host_callback)(void *, const char *, int);
typedef struct runtime {
    lua_State *L;
    host_callback host;
    void *context;
    char *pending_reply;
    char *result;
    const char *source, *name;
    int source_length;
#ifdef _WIN32
    volatile LONG cancelled;
#else
    atomic_int cancelled;
#endif
} runtime;

static void release_reply(runtime *r) {
    if (!r->pending_reply) return;
#ifdef _WIN32
    CoTaskMemFree(r->pending_reply);
#else
    free(r->pending_reply);
#endif
    r->pending_reply = NULL;
}

static runtime *owner(lua_State *L) {
    lua_getfield(L, LUA_REGISTRYINDEX, "yoake.runtime");
    runtime *r = (runtime *)lua_touserdata(L, -1);
    lua_pop(L, 1);
    return r;
}

static void cancellation_hook(lua_State *L, lua_Debug *ar) {
    runtime *r = owner(L);
    (void)ar;
#ifdef _WIN32
    if (InterlockedCompareExchange(&r->cancelled, 0, 0))
#else
    if (atomic_load(&r->cancelled))
#endif
        luaL_error(L, "Automation execution cancelled");
}

static int dispatch(lua_State *L) {
    runtime *r = owner(L);
    size_t length;
    const char *request = luaL_checklstring(L, 1, &length);
    if (length > 0x7fffffff) return luaL_error(L, "Automation request too large");
    release_reply(r);
    /* No Lua C API calls occur in managed code. Its callback returns before
       allocations/errors below can unwind to the native protected frame. */
    r->pending_reply = r->host(r->context, request, (int)length);
    if (!r->pending_reply) return luaL_error(L, "Automation host could not return a response");
    lua_pushstring(L, r->pending_reply);
    release_reply(r);
    return 1;
}

static int traceback(lua_State *L) {
    const char *message = lua_tostring(L, 1);
    if (!message) { lua_pushvalue(L, 1); return 1; }
    luaL_traceback(L, L, message, 1);
    return 1;
}

static int initialize(lua_State *L) {
    runtime *r = (runtime *)lua_touserdata(L, 1);
    luaL_openlibs(L);
    lua_pushlightuserdata(L, r);
    lua_setfield(L, LUA_REGISTRYINDEX, "yoake.runtime");
    lua_pushcfunction(L, dispatch);
    lua_setglobal(L, "__yoake_transport");
    /* Count hooks cannot reliably interrupt compiled tight loops. Disable
       Lua JIT for this cancellable host; this does not affect managed AOT. */
    luaJIT_setmode(L, 0, LUAJIT_MODE_ENGINE | LUAJIT_MODE_OFF);
    return 0;
}

static int execute(lua_State *L) {
    runtime *r = (runtime *)lua_touserdata(L, 1);
    lua_settop(L, 0);
    lua_pushcfunction(L, traceback);
    if (luaL_loadbuffer(L, r->source, (size_t)r->source_length, r->name)) return lua_error(L);
    if (lua_pcall(L, 0, 1, 1)) return lua_error(L);
    if (lua_type(L, -1) == LUA_TSTRING) {
        size_t length;
        const char *value = lua_tolstring(L, -1, &length);
        r->result = (char *)malloc(length + 1);
        if (!r->result) return luaL_error(L, "Out of memory returning Automation result");
        memcpy(r->result, value, length); r->result[length] = 0;
    }
    return 0;
}

API runtime *ya_create(host_callback host, void *context) {
    runtime *r = (runtime *)calloc(1, sizeof(runtime));
    if (!r) return NULL;
    r->host = host; r->context = context; r->L = luaL_newstate();
    if (!r->L) { free(r); return NULL; }
    if (lua_cpcall(r->L, initialize, r)) { lua_close(r->L); free(r); return NULL; }
    return r;
}

API void ya_cancel(runtime *r) {
#ifdef _WIN32
    InterlockedExchange(&r->cancelled, 1);
#else
    atomic_store(&r->cancelled, 1);
#endif
}

API void ya_reset_cancellation(runtime *r) {
#ifdef _WIN32
    InterlockedExchange(&r->cancelled, 0);
#else
    atomic_store(&r->cancelled, 0);
#endif
}

/* Return strings stay owned by the runtime until its next call/disposal. */
API int ya_execute(runtime *r, const char *source, int length, const char *name, const char **result) {
    free(r->result); r->result = NULL; release_reply(r);
    r->source = source; r->source_length = length; r->name = name;
    lua_settop(r->L, 0);
    lua_sethook(r->L, cancellation_hook, LUA_MASKCOUNT, 10000);
    int status = lua_cpcall(r->L, execute, r);
    lua_sethook(r->L, NULL, 0, 0);
    release_reply(r);
    *result = status ? (lua_type(r->L, -1) == LUA_TSTRING ? lua_tostring(r->L, -1) : "Lua error without a string message") : r->result;
    return status;
}

API void ya_destroy(runtime *r) {
    if (!r) return;
    lua_close(r->L); release_reply(r); free(r->result); free(r);
}
