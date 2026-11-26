namespace Yoake.Core.Subtitles;

using static VisualGeometry;

// One isolated drawing's glyph plane, expressed in ASS script pixels.
public readonly struct AssDrawingProjection
{
    private readonly AssPoint _position, _origin;
    private readonly double _xx, _xy, _xz, _yx, _yy, _yz, _camera;
    private readonly double _scaleX, _scaleY, _shearX, _shearY;
    private readonly AssPoint _alignment;

    public AssDrawingProjection(AssPoint position, AssPoint origin, AssTransform transform,
        AssBox bounds, int alignment, double layoutScale)
    {
        _position = position;
        _origin = origin;
        _scaleX = transform.ScaleX / 100;
        _scaleY = transform.ScaleY / 100;
        _shearX = transform.ShearX;
        _shearY = transform.ShearY;
        _camera = 312.5 * layoutScale;

        var (sx, cx) = Math.SinCos(-transform.X * Math.PI / 180);
        var (sy, cy) = Math.SinCos(transform.Y * Math.PI / 180);
        var (sz, cz) = Math.SinCos(-transform.Z * Math.PI / 180);
        _xx = cz * cy - sz * sx * sy;
        _xy = sz * cx;
        _xz = cz * sy + sz * sx * cy;
        _yx = -sz * cy - cz * sx * sy;
        _yy = cz * cx;
        _yz = -sz * sy + cz * sx * cy;
        _alignment = Offset(bounds, alignment);
    }

    private AssPoint Offset(AssBox bounds, int alignment) =>
        new(bounds.Width * ((alignment - 1) % 3) / 2 * _scaleX,
            bounds.Height * (2 - (alignment - 1) / 3) / 2 * _scaleY);

    private AssPoint Scale(AssPoint p) =>
        new((p.X + _shearX * p.Y) * _scaleX, (p.Y + _shearY * p.X) * _scaleY);

    private AssPoint Unscale(AssPoint p)
    {
        var x = p.X / _scaleX;
        var y = p.Y / _scaleY;
        var determinant = 1 - _shearX * _shearY;
        return new((x - _shearX * y) / determinant, (y - _shearY * x) / determinant);
    }

    public AssPoint AlignmentCorrection(AssBox bounds, int alignment) =>
        Unscale(Subtract(Offset(bounds, alignment), _alignment));

    public AssPoint Map(AssPoint point)
    {
        var p = Add(Subtract(_position, _origin), Subtract(Scale(point), _alignment));
        var depth = _camera + p.X * _xz + p.Y * _yz;
        var ratio = _camera / Math.Max(_camera / 20, depth);
        return Add(_origin, new((p.X * _xx + p.Y * _yx) * ratio, (p.X * _xy + p.Y * _yy) * ratio));
    }

    public AssPoint Unmap(AssPoint point)
    {
        var screen = Subtract(point, _origin);
        // Eliminate the perspective denominator and solve for the point on
        // the original glyph plane. Edge-on and behind-camera points have no
        // usable inverse and are rejected by the editor.
        var a = _xx - screen.X / _camera * _xz;
        var b = _yx - screen.X / _camera * _yz;
        var c = _xy - screen.Y / _camera * _xz;
        var d = _yy - screen.Y / _camera * _yz;
        var determinant = a * d - b * c;
        if (Math.Abs(determinant) < 1e-8) return new(double.NaN, double.NaN);
        var plane = new AssPoint((screen.X * d - b * screen.Y) / determinant,
            (a * screen.Y - screen.X * c) / determinant);
        if (_camera + plane.X * _xz + plane.Y * _yz < _camera / 20)
            return new(double.NaN, double.NaN);
        return Unscale(Add(Subtract(plane, Subtract(_position, _origin)), _alignment));
    }
}
