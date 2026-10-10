-- Yoake's private native-host adapter. The public surface targets Aegisub 3.2.2.
-- The managed host never evaluates or marshals raw Lua tables or Lua pointers.
local transport, compile = __yoake_transport, loadstring
local moon_location = __yoake_moon_location
__yoake_moon_location = nil
local moon_sources = {}
__yoake_transport = nil
local function quote(s)
  return '"' .. s:gsub('[%z\1-\31\\"]', function(c)
    if c == '\\' then return '\\\\' end
    if c == '"' then return '\\"' end
    return string.format('\\u%04x', c:byte())
  end) .. '"'
end
local function json(v, visited)
  local t = type(v)
  if t == 'nil' then return 'null' end
  if t == 'boolean' then return v and 'true' or 'false' end
  if t == 'number' then
    if v ~= v or v == math.huge or v == -math.huge then error('Non-finite host argument', 3) end
    return tostring(v)
  end
  if t == 'string' then return quote(v) end
  if t ~= 'table' then error('Unsupported host argument: '..t, 3) end
  visited = visited or {}
  if visited[v] then error('Cyclic host argument', 3) end
  visited[v] = true
  local parts = {}
  for k, value in pairs(v) do
    if type(k) ~= 'string' and type(k) ~= 'number' then error('Unsupported host table key', 3) end
    parts[#parts+1] = quote(tostring(k)) .. ':' .. json(value, visited)
  end
  visited[v] = nil
  return '{' .. table.concat(parts, ',') .. '}'
end
local function host(op, ...)
  local args = {n=select('#', ...), ...}
  local response = transport(json({op=op, args=args}))
  local fn, err = compile(response, '=Yoake host response')
  if not fn then error(err, 2) end
  local reply = fn()
  if not reply.ok then error(reply.error, 2) end
  return unpack(reply.values, 1, reply.values.n)
end

dofile, loadfile = nil, nil
if jit then jit.on = function() end end -- retain cancellable interpreter hooks
local function load_source(path)
  local text = host('read_file', path)
  if path:lower():sub(-5) == '.moon' then
    moon_sources['@'..path] = text
    moon_location(true)
    return require('moonscript').loadstring(text, '@'..path)
  end
  return compile(text, '@'..path)
end
function include(name)
  local path = host('include_path', name)
  local fn, err = load_source(path)
  if not fn then error('Error loading Lua include "'..name..'":\n'..err, 2) end
  return fn()
end
package.path = host('package_path')
package.loaders[2] = function(name)
  local path = host('module_path', name, package.path)
  if not path then return "\n\tno Automation module '"..name.."'" end
  local fn, err = load_source(path)
  if not fn then error('Error loading Lua module "'..path..'":\n'..err, 2) end
  return fn
end

local features = {}
aegisub = {lua_automation_version=4}
local function feature(name, help, run, validate, active, filter, priority)
  if type(name) ~= 'string' and type(name) ~= 'number' then error('Feature name must be a string', 3) end
  if type(run) ~= 'function' then error('The processing function must be a function', 3) end
  local i = #features+1
  features[i] = {run=run, validate=not filter and validate or nil,
    config=filter and validate or nil, isactive=active, filter=filter}
  host(filter and 'register_filter' or 'register_macro', i, tostring(name), tostring(help or ''),
    type(validate)=='function', type(active)=='function', priority or 0)
end
function aegisub.register_macro(name, help, run, validate, active)
  feature(name, help, run, validate, active, false)
end
function aegisub.register_filter(name, help, priority, run, config)
  feature(name, help, run, config, nil, true, tonumber(priority) or 0)
end
local script_cancelled = false
function aegisub.cancel() script_cancelled = true; error('Automation execution cancelled', 0) end
for _,name in ipairs({'text_extents','frame_from_ms','ms_from_frame','video_size','keyframes',
  'decode_path','file_name','gettext','project_properties'}) do
  local operation = name
  aegisub[operation] = function(...) return host(operation, ...) end
end
function aegisub.__init_clipboard()
  return {get=function() return host('clipboard_get') end, set=function(s) return host('clipboard_set',s) end}
end

local alive, generation, provenance = false, 0, setmetatable({}, {__mode='k'})
local subtitle_views = setmetatable({}, {__mode='k'})
local table_ipairs = ipairs
function ipairs(value)
  if subtitle_views[value] then return getmetatable(value).__ipairs() end
  return table_ipairs(value)
end
-- AssEntry conversion ignores authoring helpers (kara, styleref, cyclic script
-- data). Only transmit the documented fields, just as LuaToAssEntry reads them.
local line_fields = {}
for name in ('class raw section key value comment layer start_time end_time style actor margin_l margin_r margin_t margin_b effect text extra name fontname fontsize color1 color2 color3 color4 bold italic underline strikeout scale_x scale_y spacing angle borderstyle outline shadow align encoding relative_to'):gmatch('%S+') do
  line_fields[name] = true
end
local function line_argument(line)
  if type(line) ~= 'table' then return line end
  local fields = {}
  for key in pairs(line_fields) do fields[key] = line[key] end
  return fields
end
aegisub.text_extents = function(style,text) return host('text_extents',line_argument(style),text) end
local function check(current) if not alive or current ~= generation then error('Subtitles object is no longer valid', 3) end end
local function write(i,line,current)
  check(current)
  return host('subs_write',i,line_argument(line),line and provenance[line])
end
local function subtitles()
  local current = generation
  local function valid() check(current) end
  local subs = newproxy(true)
  subtitle_views[subs] = true
  local mt = getmetatable(subs)
  mt.__len = function() valid(); return host('subs_count') end
  mt.__index = function(_,key)
    valid()
    if type(key)=='number' then
      local line, id = host('subs_read',key)
      provenance[line] = id
      return line
    end
    if key=='n' then return host('subs_count') end
    if key=='delete' then return function(...) valid(); return host('subs_delete',...) end end
    if key=='deleterange' then return function(a,b) valid(); return host('subs_deleterange',a,b) end end
    if key=='append' or key=='insert' then
      return function(...)
        valid()
        local values = {n=select('#',...),...}
        local ids = {}
        for i=1,values.n do
          if type(values[i])=='table' then ids[i]=provenance[values[i]]; values[i]=line_argument(values[i]) end
        end
        return host('subs_'..key,values,ids)
      end
    end
    error('Invalid indexing in Subtitle File object: '..tostring(key),2)
  end
  mt.__newindex = function(_,i,line) write(i,line,current) end
  mt.__ipairs = function()
    valid()
    local function next_line(_,i)
      i=i+1
      if i > #subs then return nil end
      return i,subs[i]
    end
    return next_line,nil,0
  end
  return subs
end

local function log(...)
  local args={...}; local level=3
  if type(args[1])=='number' then level=table.remove(args,1) end
  local text = #args>1 and string.format(unpack(args)) or tostring(args[1] or '')
  return host('log',text,level)
end
local function script_traceback(message)
  local lines = {tostring(message), 'stack traceback:'}
  local tables = package.loaded['moonscript.line_tables'] or {}
  local seen = {}
  for level=2,200 do
    local info=debug.getinfo(level,'Sln')
    if not info then break end
    if info.what=='C' then
      lines[#lines+1]='\t[C]: '..(info.name and "in function '"..info.name.."'" or 'native call')
    else
      local line=info.currentline
      local source=info.source
      seen[source] = true
      local original=moon_sources[source]
      local positions=tables[source]
      local position=positions and positions[line]
      -- Compiler bookkeeping lines can have no direct entry. The bundled
      -- MoonScript error mapper searches backward to the owning statement.
      if original and positions and not position then
        for generated=line-1,0,-1 do
          if positions[generated] then position=positions[generated]; break end
        end
      end
      if original and position then
        local _,count=original:sub(1,position):gsub('\n','\n'); line=count+1
      end
      lines[#lines+1]='\t'..source:gsub('^@','')..':'..line..': '..(info.name and "in function '"..info.name.."'" or 'in script')
    end
  end
  local source,line = moon_location()
  if source and not seen[source] then
    local original,positions = moon_sources[source],tables[source]
    local position = positions and positions[line]
    if original and positions and not position then
      for generated=line-1,0,-1 do
        if positions[generated] then position=positions[generated]; break end
      end
    end
    if original and position then
      local _,count=original:sub(1,position):gsub('\n','\n'); line=count+1
    end
    lines[#lines+1]='\t'..source:gsub('^@','')..':'..line..': last MoonScript call site'
  end
  return table.concat(lines,'\n')
end
function __yoake_invoke(index, method, settings)
  moon_location(false)
  script_cancelled = false
  generation = generation + 1
  provenance = setmetatable({}, {__mode='k'})
  alive = true
  local current = generation
  local feature = assert(features[index], 'Unknown Automation feature')
  local processing = method=='run'
  aegisub.set_undo_point = not feature.filter and processing and function(name) check(current); return host('undo_point',name) end or nil
  aegisub.parse_karaoke_data = function(line) check(current); return host('karaoke',line_argument(line)) end
  aegisub.progress = processing and {
    set=function(p) check(current); return host('progress',p) end,
    task=function(t) check(current); return host('progress',nil,t) end,
    title=function(t) check(current); return host('progress',nil,nil,t) end,
    is_cancelled=function() check(current); return host('is_cancelled') end
  } or nil
  aegisub.log, aegisub.debug = log,processing and {out=log} or nil
  aegisub.dialog = processing and not feature.filter and {
    display=function(...) check(current); return host('dialog',...) end,
    open=function(...) check(current); return host('file_dialog_open',...) end,
    save=function(...) check(current); return host('file_dialog_save',...) end
  } or nil
  local selected, active = host('selection')
  local fn = feature[method]
  local ok,a,b = xpcall(function()
    if feature.filter then return fn(subtitles(),settings or {}) end
    return fn(subtitles(),selected,active)
  end,script_traceback)
  alive=false
  aegisub.progress, aegisub.debug, aegisub.dialog = nil,nil,nil
  if script_cancelled then return json({cancelled=true}) end
  if not ok then error(a,0) end
  return json({first=a,second=b})
end
function __yoake_metadata()
  if tonumber(version)==3 then error('Automation 3 is no longer supported') end
  return json({name=script_name,description=script_description,author=script_author,version=script_version})
end
