-- Yoake test adapter for the unmodified 3.2.2 MoonScript module assertions.
-- This is a small assertion runner, not a replacement for the tested modules.
local builtin_assert = assert
local checks, failures, group = 0, {}, ''
local function equal(expected, actual)
  builtin_assert(expected == actual, 'expected '..tostring(expected)..', got '..tostring(actual))
end
assert = setmetatable({is={
  equal=equal,
  ['nil']=function(value) builtin_assert(value==nil,'expected nil, got '..tostring(value)) end,
  ['not']={['nil']=function(value) builtin_assert(value~=nil,'expected a non-nil value') end},
  error=function(fn) local ok=pcall(fn); builtin_assert(not ok,'expected a Lua error') end
}}, {__call=function(_,...) return builtin_assert(...) end})
function describe(name, fn)
  local old = group; group=group..'/'..name; fn(); group=old
end
function it(name, fn)
  checks=checks+1
  local ok, err=xpcall(fn,debug.traceback)
  if not ok then failures[#failures+1]=group..'/'..name..': '..err end
end
function finish_module_tests(expected)
  builtin_assert(checks==expected,'fixture assertion count changed: '..checks..' / '..expected)
  builtin_assert(#failures==0,table.concat(failures,'\n\n'))
end
