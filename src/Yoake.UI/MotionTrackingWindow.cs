using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.UI.ViewModels;
using Yoake.Native;

namespace Yoake.UI;

public sealed class MotionTrackingWindow : Window
{
    private readonly MotionTrackingSession _session;
    private readonly TrackingPreview _preview;
    private readonly Slider _frames;
    private readonly TextBlock _status = new(), _frameLabel = new();
    private readonly NumericUpDown _patternWidth, _patternHeight, _searchWidth, _searchHeight;
    private readonly ComboBox _viewable = new() { ItemsSource=new[]{"Fast","Viewable"}, SelectedIndex=0, Width=100 };
    private readonly CheckBox _normalize=new(){Content="Normalize",IsChecked=true}, _trail=new(){Content="Show track trail",IsChecked=true};
    private readonly NumericUpDown _threshold;
    private readonly Button _play;
    private readonly TrackingGraph _graph;
    private readonly ToggleButton _subtitle,_clip;
    private readonly TextBlock _reference=new();
    private readonly ComboBox _mode;
    private readonly CheckBox[] _applyOptions;
    private bool _updating;
    public MotionTrackingWindow(MainWindowViewModel model)
    {
        _session = model.MotionTracking ?? throw new InvalidOperationException("No tracking session.");
        Name = "MotionTrackingWindow"; Title = "Motion Track"; Width = 780; Height = 760; MinWidth = 620; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var names = new NameScope();
        NameScope.SetNameScope(this, names);
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,Auto,*,92,Auto"), Margin = new(8), RowSpacing = 5 };
        void Place(Control c,int row){Grid.SetRow(c,row);root.Children.Add(c);}
        Button Action(string label,string id,string? glyph=null)
        {
            var button=new Button{Name="MotionTrack_"+id,Content=glyph is null?label:new MotionGlyph(glyph),Command=model.Actions["motion-track/"+id],Padding=new(6,3),MinWidth=30,MinHeight=26};
            ToolTip.SetTip(button,label);Avalonia.Automation.AutomationProperties.SetName(button,label);names.Register(button.Name!,button);return button;
        }
        Place(_frameLabel,0);
        var workflow=new Grid{ColumnDefinitions=new("Auto,*,Auto,Auto"),ColumnSpacing=4};
        workflow.Children.Add(Action("Track Motion","both"));
        _reference.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(_reference,1);workflow.Children.Add(_reference);
        _subtitle=new ToggleButton{Content="Subtitle",IsChecked=true};_clip=new ToggleButton{Content=@"\clip"};
        _subtitle.Click+=(_,_)=>{if(!_updating)model.Actions["motion-track/channel"].Execute(false);Refresh();};
        _clip.Click+=(_,_)=>{if(!_updating)model.Actions["motion-track/channel"].Execute(true);Refresh();};
        Grid.SetColumn(_subtitle,2);workflow.Children.Add(_subtitle);Grid.SetColumn(_clip,3);workflow.Children.Add(_clip);Place(workflow,1);
        _preview = new TrackingPreview(_session, region => model.Actions["motion-track/pattern"].Execute(region));
        _preview.Name = "MotionTrackPreview";
        names.Register(_preview.Name!, _preview);
        Place(_preview,5);
        _trail.IsCheckedChanged+=(_,_)=>{_preview.ShowTrail=_trail.IsChecked==true;_preview.InvalidateVisual();};
        _frames = new Slider { Name="MotionTrackTimeline", Minimum = _session.FirstFrame, Maximum = _session.LastFrame, TickFrequency = 1, IsSnapToTickEnabled = true };
        names.Register(_frames.Name!,_frames);
        _frames.PropertyChanged += (_, e) => { if (!_updating && e.Property == Slider.ValueProperty) model.Actions["motion-track/frame"].Execute((int)Math.Round(_frames.Value)); };
        Place(_frames,2);
        var transport=new StackPanel{Name="MotionTrackTransport",Orientation=Orientation.Horizontal,Spacing=4};
        names.Register(transport.Name!,transport);
        _play=new Button{Name="MotionTrackPlay",Content=new MotionGlyph("play"),Padding=new(6,3),MinWidth=30,MinHeight=26};
        names.Register(_play.Name!,_play);
        _play.Click+=(_,_)=>model.Actions[_session.Busy?"motion-track/stop":"motion-track/play"].Execute(null);
        transport.Children.Add(_play);
        foreach(var (label,id,glyph) in new[]{("Track to Start","backward","backward"),("Track Previous","backward-frame","backward-frame"),("Track Next","forward-frame","forward-frame"),("Track to End","forward","forward")})
            transport.Children.Add(Action(label,id,glyph));
        foreach(var (label,step,glyph) in new[]{("Inspect previous frame without tracking",-1,"previous"),("Inspect next frame without tracking",1,"next")})
        {
            var button=new Button{Content=new MotionGlyph(glyph),Padding=new(6,3),MinWidth=30,MinHeight=26};
            ToolTip.SetTip(button,label);Avalonia.Automation.AutomationProperties.SetName(button,label);
            button.Click+=(_,_)=>model.Actions["motion-track/frame"].Execute(_session.CurrentFrame+step);transport.Children.Add(button);
        }
        transport.Children.Add(Action("Set reference","reference","reference"));Place(transport,3);
        var settings = new WrapPanel { Orientation = Orientation.Horizontal };
        _mode = new ComboBox { Width = 165, ItemsSource = new[] { "Movement only", "Movement + rotation", "Movement + size", "Movement + size + rotation" }, SelectedIndex = 0 };
        _mode.SelectionChanged += (_, _) => { if (!_updating && _mode.SelectedIndex >= 0) model.Actions["motion-track/model"].Execute((MotionModel)_mode.SelectedIndex); };
        _patternWidth = Number("Pattern W", _session.Region.Width, 8, 256, settings);
        _patternHeight = Number("H", _session.Region.Height, 8, 256, settings);
        _searchWidth = Number("Search W", _session.Region.SearchWidth, 12, 512, settings);
        _searchHeight = Number("H", _session.Region.SearchHeight, 12, 512, settings);
        settings.Children.Add(new TextBlock{Text="Threshold:",VerticalAlignment=VerticalAlignment.Center,Margin=new(4,0)});
        _threshold=new NumericUpDown{Value=.75m,Minimum=0,Maximum=1,Increment=.05m,FormatString="0.00",Width=76,Padding=new(3,0)};
        settings.Children.Add(_threshold);settings.Children.Add(_normalize);
        _threshold.ValueChanged+=(_,_)=>{if(!_updating&&!_session.Busy&&_threshold.Value is {} value)_session.Threshold=(double)value;};
        _normalize.IsCheckedChanged+=(_,_)=>{if(!_updating&&!_session.Busy)_session.Normalize=_normalize.IsChecked==true;};
        settings.Children.Add(new TextBlock{Text="Output:",VerticalAlignment=VerticalAlignment.Center,Margin=new(4,0)});
        settings.Children.Add(_mode);
        settings.Children.Add(new TextBlock{Text="View:",VerticalAlignment=VerticalAlignment.Center,Margin=new(4,0)});
        settings.Children.Add(_viewable);settings.Children.Add(_trail);
        foreach (var number in new[] { _patternWidth, _patternHeight, _searchWidth, _searchHeight })
            number.ValueChanged += (_, _) =>
            {
                if (_updating || _session.Busy) return;
                var point = _session.CurrentPoint;
                var region = _session.Region with { X = point?.X ?? _session.Region.X, Y = point?.Y ?? _session.Region.Y,
                    Width = (int)(_patternWidth.Value ?? 32), Height = (int)(_patternHeight.Value ?? 32), SearchWidth = (int)(_searchWidth.Value ?? 96), SearchHeight = (int)(_searchHeight.Value ?? 96) };
                model.Actions["motion-track/pattern"].Execute(region);
            };
        _viewable.SelectionChanged += (_, _) => { if (!_updating) model.Actions["motion-track/viewable"].Execute(_viewable.SelectedIndex == 1); };
        Place(settings,4);
        _graph=new TrackingGraph(_session){Name="MotionTrackGraph"};names.Register(_graph.Name!,_graph);Place(_graph,6);
        var footer = new StackPanel { Spacing = 6 };
        _status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(_status);
        _applyOptions = new[] { new CheckBox { Content = "X", IsChecked = true }, new CheckBox { Content = "Y", IsChecked = true },
            new CheckBox { Content = "Size", IsChecked = true }, new CheckBox { Content = "Rotation", IsChecked = true },
            new CheckBox { Content = "Optimize position", IsChecked = false } };
        var advanced = new WrapPanel();
        foreach (var option in _applyOptions)
        {
            option.Margin = new(0, 0, 12, 0); advanced.Children.Add(option);
            option.IsCheckedChanged += (_, _) => { if (!_updating) model.Actions["motion-track/options"].Execute(new MotionApplyOptions(
                _applyOptions[0].IsChecked == true, _applyOptions[1].IsChecked == true, _applyOptions[2].IsChecked == true,
                _applyOptions[3].IsChecked == true, _applyOptions[4].IsChecked == true)); };
        }
        footer.Children.Add(new Expander { Header = "Advanced apply", Content = advanced });
        var actions = new WrapPanel();
        actions.Children.Add(Action("Apply","apply"));
        actions.Children.Add(Action("Reset tracking","reset","reset"));
        var close = new Button { Content = "Close", IsCancel = true }; close.Click += (_, _) => Close(); actions.Children.Add(close);
        footer.Children.Add(actions); Place(footer,7); Content = root;
        names.Complete();
        _session.PropertyChanged += SessionChanged; model.MotionApplied += Applied; model.Registry.CommandFailed += CommandFailed;
        Closed += (_, _) => { _session.Stop(); _session.PropertyChanged -= SessionChanged; model.MotionApplied -= Applied; model.Registry.CommandFailed -= CommandFailed; };
        Refresh();
    }
    private static NumericUpDown Number(string label, int value, int minimum, int maximum, Panel panel)
    {
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0) });
        var number = new NumericUpDown { Value = value, Minimum = minimum, Maximum = maximum, Increment = 2, Width = 70, FormatString = "0", Padding = new(3, 0) };
        panel.Children.Add(number); return number;
    }
    private void SessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    private void CommandFailed(object? sender, CommandFailureEventArgs e)
    {
        if (e.CommandId.StartsWith("motion-track/", StringComparison.Ordinal)) _status.Text = e.Exception.Message;
    }
    private void Applied(object? sender, EventArgs args) => Close();
    private void Refresh()
    {
        _updating = true;
        try
        {
            _frames.Value = _session.CurrentFrame; _frames.IsEnabled = !_session.Busy;
            _frameLabel.Text = $"Selected line frames: {_session.FirstFrame} – {_session.LastFrame}   ·   Current: {_session.CurrentFrame}   ·   Tracked: {_session.Points.Count}/{_session.LastFrame-_session.FirstFrame+1}";
            _reference.Text=$"Reference: {_session.ReferenceFrame}";
            _status.Text = _session.Status;
            _patternWidth.Value = _session.Region.Width; _patternHeight.Value = _session.Region.Height;
            _searchWidth.Value = _session.Region.SearchWidth; _searchHeight.Value = _session.Region.SearchHeight;
            _subtitle.IsChecked=!_session.TrackClip;_clip.IsChecked=_session.TrackClip;
            _viewable.SelectedIndex=_session.Viewable?1:0;_mode.SelectedIndex=(int)_session.Model;
            var kind=_session.Busy?"stop":"play";if(_play.Content is not MotionGlyph glyph||glyph.Kind!=kind)_play.Content=new MotionGlyph(kind);
            var tip=_session.Busy?"Stop current operation":"Play selected frame range";ToolTip.SetTip(_play,tip);Avalonia.Automation.AutomationProperties.SetName(_play,tip);
            foreach (var control in new Control[] { _patternWidth, _patternHeight, _searchWidth, _searchHeight, _viewable, _mode, _subtitle,_clip,_threshold,_normalize }) control.IsEnabled = !_session.Busy;
            foreach (var option in _applyOptions) option.IsEnabled = !_session.Busy;
            _preview.InvalidateVisual();_graph.InvalidateVisual();
        }
        finally { _updating = false; }
    }

    private sealed class MotionGlyph(string kind):Control
    {
        public string Kind=>kind;
        protected override Size MeasureOverride(Size availableSize)=>new(20,20);
        public override void Render(DrawingContext context)
        {
            IBrush Brush(string key,IBrush fallback)=>this.TryFindResource(key,ActualThemeVariant,out var value)&&value is IBrush brush?brush:fallback;
            var orange=Brush("VisualToolPrimaryBrush",Brushes.Orange);var neutral=Brush("IconForegroundBrush",Brushes.White);var red=Brush("VisualToolWarningBrush",Brushes.IndianRed);
            using var shift=context.PushTransform(Matrix.CreateTranslation((Bounds.Width-20)/2,(Bounds.Height-20)/2));
            void Fill(string path,IBrush brush)=>context.DrawGeometry(brush,null,Geometry.Parse(path));
            void Stroke(string path,IBrush brush)=>context.DrawGeometry(null,new Pen(brush,1.6),Geometry.Parse(path));
            switch(kind)
            {
                case "play":Fill("M6 3 L16 10 L6 17 Z",neutral);break;
                case "stop":Fill("M5 5 H15 V15 H5 Z",red);break;
                case "previous":Fill("M14 4 L5 10 L14 16 Z",neutral);break;
                case "next":Fill("M6 4 L15 10 L6 16 Z",neutral);break;
                case "backward":Fill("M9 5 L3 10 L9 15 Z M16 5 L10 10 L16 15 Z",orange);Stroke("M1 3 V17",orange);break;
                case "forward":Fill("M4 5 L10 10 L4 15 Z M11 5 L17 10 L11 15 Z",orange);Stroke("M19 3 V17",orange);break;
                case "backward-frame":Fill("M15 4 L6 10 L15 16 Z",orange);Stroke("M3 3 V17",orange);break;
                case "forward-frame":Fill("M5 4 L14 10 L5 16 Z",orange);Stroke("M17 3 V17",orange);break;
                case "reference":Stroke("M5 5 H15 V15 H5 Z M10 1 V5 M10 15 V19 M1 10 H5 M15 10 H19",orange);context.DrawEllipse(orange,null,new Point(10,10),1.5,1.5);break;
                case "reset":Stroke("M5 6 A7 7 0 1 1 4 14 M5 2 V7 H10",red);break;
            }
        }
    }
    private sealed class TrackingGraph(MotionTrackingSession session):Control
    {
        public override void Render(DrawingContext context)
        {
            var points=session.Points;var w=Bounds.Width;var h=Bounds.Height;if(w<60||h<20)return;
            var bg=this.TryFindResource("PanelBrush",ActualThemeVariant,out var b)&&b is IBrush brush?brush:Brushes.Black;
            var fg=this.TryFindResource("IconForegroundBrush",ActualThemeVariant,out var f)&&f is IBrush foreground?foreground:Brushes.White;
            var orange=this.TryFindResource("VisualToolPrimaryBrush",ActualThemeVariant,out var o)&&o is IBrush primary?primary:Brushes.Orange;
            var blue=this.TryFindResource("VisualToolDetailBrush",ActualThemeVariant,out var d)&&d is IBrush detail?detail:Brushes.DeepSkyBlue;
            context.DrawRectangle(bg,new Pen(Brushes.Gray,.6),new Rect(Bounds.Size));
            var rows=new (string Name,Func<TrackingPoint,double> Read,IBrush Color)[]{
                ("X",p=>p.X,orange),("Y",p=>p.Y,blue),("Scale",p=>p.Scale,orange),("Rot",p=>p.Rotation,blue),("Conf",p=>p.Confidence,blue)};
            var lane=h/rows.Length;
            double X(int frame)=>48+(w-52)*(frame-session.FirstFrame)/Math.Max(1,session.LastFrame-session.FirstFrame);
            for(int i=0;i<rows.Length;i++)
            {
                var row=rows[i];var y=i*lane;
                var label=new FormattedText(row.Name,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Typeface.Default,10,fg);
                context.DrawText(label,new Point(5,y+2));context.DrawLine(new Pen(Brushes.Gray,.4),new Point(45,y+lane),new Point(w,y+lane));
                if(points.Count<2)continue;
                var min=points.Min(row.Read);var max=points.Max(row.Read);
                double Y(TrackingPoint p)=>y+lane-3-(lane-6)*(max-min<1e-9?.5:(row.Read(p)-min)/(max-min));
                for(int j=1;j<points.Count;j++)if(points[j].Frame==points[j-1].Frame+1)
                    context.DrawLine(new Pen(row.Color,1.1),new Point(X(points[j-1].Frame),Y(points[j-1])),new Point(X(points[j].Frame),Y(points[j])));
            }
            var current=X(session.CurrentFrame);context.DrawLine(new Pen(fg,.8),new Point(current,0),new Point(current,h));
        }
    }

    private sealed class TrackingPreview(MotionTrackingSession session, Action<TrackingRegion> commit) : Control
    {
        public bool ShowTrail{get;set;}=true;
        private Point? _start;
        private TrackingRegion? _drag;
        private Rect ImageRect
        {
            get
            {
                var height = Math.Min(Bounds.Height, Bounds.Width / session.AspectRatio);
                var width = height * session.AspectRatio;
                return new((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
            }
        }
        private Point ImagePoint(Point point)
        {
            var rect = ImageRect;
            return new(Math.Clamp((point.X - rect.X) * session.Width / Math.Max(1, rect.Width), 0, session.Width),
                Math.Clamp((point.Y - rect.Y) * session.Height / Math.Max(1, rect.Height), 0, session.Height));
        }
        public override void Render(DrawingContext context)
        {
            base.Render(context); context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            var rect = ImageRect;
            if (session.Preview is {} image) context.DrawImage(image, new Rect(image.Size), rect);
            var current = session.CurrentPoint;
            var region = _drag ?? session.Region with { X = current?.X ?? session.Region.X, Y = current?.Y ?? session.Region.Y };
            Point Screen(double x, double y) => new(rect.X + x * rect.Width / session.Width, rect.Y + y * rect.Height / session.Height);
            using (context.PushClip(rect))
            {
                Point? last = null;
                foreach (var p in ShowTrail?session.Points:Array.Empty<TrackingPoint>())
                {
                    var next = Screen(p.X, p.Y);
                    if (last is {} a) context.DrawLine(new Pen(Brushes.Orange, 1), a, next);
                    last = next;
                }
                var center = Screen(region.X, region.Y);
                var search = new Rect(Screen(region.X - region.SearchWidth / 2d, region.Y - region.SearchHeight / 2d), Screen(region.X + region.SearchWidth / 2d, region.Y + region.SearchHeight / 2d));
                var pattern = new Rect(Screen(region.X - region.Width / 2d, region.Y - region.Height / 2d), Screen(region.X + region.Width / 2d, region.Y + region.Height / 2d));
                context.DrawRectangle(null, new Pen(Brushes.Orange, 2, DashStyle.Dash), search);
                context.DrawRectangle(null, new Pen(current is { Confidence: < .65 } ? Brushes.Red : Brushes.DeepSkyBlue, 2), pattern);
                context.DrawEllipse(Brushes.White, null, center, 3, 3);
            }
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (session.Busy || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !ImageRect.Contains(e.GetPosition(this))) return;
            _start = ImagePoint(e.GetPosition(this)); e.Pointer.Capture(this); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e); if (_start is not {} start) return;
            var end = ImagePoint(e.GetPosition(this));
            var width = Math.Clamp((int)Math.Abs(end.X - start.X), 8, 256); var height = Math.Clamp((int)Math.Abs(end.Y - start.Y), 8, 256);
            _drag = session.Region with { X = Math.Clamp((start.X + end.X) / 2, width / 2d, session.Width - width / 2d), Y = Math.Clamp((start.Y + end.Y) / 2, height / 2d, session.Height - height / 2d),
                Width = width, Height = height, SearchWidth = Math.Max(width + 4, session.Region.SearchWidth), SearchHeight = Math.Max(height + 4, session.Region.SearchHeight) };
            InvalidateVisual();
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e); if (_start is not {} start) return;
            var region = _drag ?? session.Region with { X = Math.Clamp(start.X, session.Region.Width / 2d, session.Width - session.Region.Width / 2d), Y = Math.Clamp(start.Y, session.Region.Height / 2d, session.Height - session.Region.Height / 2d) };
            _start = null; _drag = null; e.Pointer.Capture(null); commit(region); InvalidateVisual(); e.Handled = true;
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _start = null; _drag = null; InvalidateVisual(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _start is not null) { _start = null; _drag = null; InvalidateVisual(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }
    }
}
