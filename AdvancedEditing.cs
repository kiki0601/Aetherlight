using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Aetherlight;

public partial class MainWindow
{
    private readonly DispatcherTimer _fastPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private CancellationTokenSource? _renderCts;
    private int _renderVersion;
    private bool _fastPipelineInstalled;
    private bool _draggingCurve;
    private bool _draggingWheel;
    private bool _draggingAdvancedMask;
    private string _curveChannel = "L";
    private readonly Dictionary<string, List<Point>> _curves = new()
    {
        ["L"] = new() { new Point(0,0), new Point(1,1) },
        ["R"] = new() { new Point(0,0), new Point(1,1) },
        ["G"] = new() { new Point(0,0), new Point(1,1) },
        ["B"] = new() { new Point(0,0), new Point(1,1) }
    };
    private double _sharpening, _noiseReduction, _clarity, _texture, _dehaze, _vignette, _grain, _glow, _halation;
    private double _gradeHue, _gradeSat, _gradeLuma, _gradeBlend = 1, _gradeBalance;
    private string _gradeRange = "M";
    private bool _brushMaskEnabled, _linearMaskEnabled, _radialMaskEnabled, _autoSkyMaskEnabled, _autoSubjectMaskEnabled;
    private Point _maskDragStart, _maskDragEnd;
    private double _radialRadius = .25;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InstallFastPipeline();
        DrawCurve();
        DrawColorWheel();
    }

    private void InstallFastPipeline()
    {
        if (_fastPipelineInstalled) return;
        _fastPipelineInstalled = true;
        _fastPreviewTimer.Tick += (_, _) => StartFastRender();
        Slider[] basic = { ExposureSlider, ContrastSlider, HighlightsSlider, ShadowsSlider, WhitesSlider, BlacksSlider, TemperatureSlider, TintSlider, VibranceSlider, SaturationSlider };
        foreach (var s in basic)
        {
            s.ValueChanged -= Adjustment_ValueChanged;
            s.ValueChanged += FastBasic_ValueChanged;
            s.PreviewMouseUp += (_, _) => StartFastRender();
        }
        ExportButton.Click -= Export_Click;
        ExportButton.Click += ExportAdvanced_Click;
        SharpeningSlider.ValueChanged += AdvancedSliderChanged;
        NoiseReductionSlider.ValueChanged += AdvancedSliderChanged;
        ClaritySlider.ValueChanged += AdvancedSliderChanged;
        TextureSlider.ValueChanged += AdvancedSliderChanged;
        DehazeSlider.ValueChanged += AdvancedSliderChanged;
        VignetteSlider.ValueChanged += AdvancedSliderChanged;
        GrainSlider.ValueChanged += AdvancedSliderChanged;
        GlowSlider.ValueChanged += AdvancedSliderChanged;
        HalationSlider.ValueChanged += AdvancedSliderChanged;
        GradeBlendSlider.ValueChanged += AdvancedSliderChanged;
        GradeBalanceSlider.ValueChanged += AdvancedSliderChanged;
        MaskSizeSlider.ValueChanged += AdvancedMaskChanged;
        MaskExposureSlider.ValueChanged += AdvancedMaskChanged;
        DevelopPreview.MouseLeftButtonDown += AdvancedPreviewDown;
        DevelopPreview.MouseMove += AdvancedPreviewMove;
        DevelopPreview.MouseLeftButtonUp += AdvancedPreviewUp;
    }

    private void FastBasic_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        UpdateValueLabels();
        _fastPreviewTimer.Stop();
        _fastPreviewTimer.Start();
    }

    private void AdvancedSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        _sharpening = SharpeningSlider.Value; _noiseReduction = NoiseReductionSlider.Value; _clarity = ClaritySlider.Value;
        _texture = TextureSlider.Value; _dehaze = DehazeSlider.Value; _vignette = VignetteSlider.Value; _grain = GrainSlider.Value;
        _glow = GlowSlider.Value; _halation = HalationSlider.Value; _gradeBlend = GradeBlendSlider.Value; _gradeBalance = GradeBalanceSlider.Value;
        _fastPreviewTimer.Stop(); _fastPreviewTimer.Start();
    }

    private void AdvancedMaskChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        DrawAdvancedMaskOverlay(); _fastPreviewTimer.Stop(); _fastPreviewTimer.Start();
    }

    private void StartFastRender()
    {
        _fastPreviewTimer.Stop();
        if (_originalPixels == null || _pixelWidth == 0) return;
        int version = ++_renderVersion;
        _renderCts?.Cancel(); _renderCts?.Dispose(); _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        byte[] source = _originalPixels;
        int sw = _pixelWidth, sh = _pixelHeight;
        double[] v = { ExposureSlider.Value, ContrastSlider.Value, HighlightsSlider.Value, ShadowsSlider.Value, WhitesSlider.Value, BlacksSlider.Value, TemperatureSlider.Value, TintSlider.Value, VibranceSlider.Value, SaturationSlider.Value };
        Snapshot state = SnapshotState(sw, sh);
        double asShot = _asShotTemperature;
        _ = Task.Run(() => RenderPreview(source, sw, sh, v, asShot, state, token), token).ContinueWith(t =>
        {
            if (t.IsCanceled || t.IsFaulted || token.IsCancellationRequested || version != _renderVersion) return;
            var r = t.Result;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token.IsCancellationRequested || version != _renderVersion) return;
                var bmp = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, r.Pixels, r.Width * 4);
                bmp.Freeze(); DevelopPreview.Source = bmp;
            }), DispatcherPriority.Render);
        }, TaskScheduler.Default);
    }

    private Snapshot SnapshotState(int sw, int sh) => new(
        _sharpening, _noiseReduction, _clarity, _texture, _dehaze, _vignette, _grain, _glow, _halation,
        _gradeHue, _gradeSat, _gradeLuma, _gradeBlend, _gradeBalance, _gradeRange,
        _curves.ToDictionary(x => x.Key, x => x.Value.ToArray()), _maskPoints.ToArray(),
        _brushMaskEnabled, _linearMaskEnabled, _radialMaskEnabled, _autoSkyMaskEnabled, _autoSubjectMaskEnabled,
        _maskDragStart, _maskDragEnd, _radialRadius, MaskSizeSlider.Value, MaskExposureSlider.Value, sw, sh);

    private static PreviewResult RenderPreview(byte[] src, int sw, int sh, double[] v, double asShot, Snapshot s, CancellationToken token)
    {
        const int maxWidth = 1280;
        int w = Math.Min(maxWidth, sw), h = Math.Max(1, (int)Math.Round(sh * (double)w / sw));
        byte[] dst = new byte[w * h * 4];
        double sx = sw / (double)w, sy = sh / (double)h;
        double exposure = Math.Pow(2, v[0]);
        double contrast = (259.0 * (v[1] + 255.0)) / (255.0 * (259.0 - v[1]));
        double saturation = 1 + v[9] / 100.0, vibrance = v[8] / 100.0;
        double temp = Math.Log(Math.Max(1, v[6]) / Math.Max(1, asShot), 2) * .10, tint = v[7] / 100.0;
        Parallel.For(0, h, new ParallelOptions { CancellationToken = token }, y =>
        {
            for (int x = 0; x < w; x++)
            {
                if ((x & 127) == 0 && token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                int ox = Math.Min(sw - 1, (int)(x * sx)), oy = Math.Min(sh - 1, (int)(y * sy)), si = (oy * sw + ox) * 4;
                double b = src[si] / 255.0, g = src[si + 1] / 255.0, r = src[si + 2] / 255.0;
                Basic(ref r, ref g, ref b, exposure, contrast, temp, tint, saturation, vibrance, v[2]/100, v[3]/100, v[4]/100, v[5]/100);
                Curve(ref r, ref g, ref b, s.Curves);
                Grade(ref r, ref g, ref b, s);
                DetailEffects(ref r, ref g, ref b, src, sw, sh, ox, oy, s);
                Mask(ref r, ref g, ref b, x, y, w, h, s);
                int di = (y * w + x) * 4; dst[di] = Byte(b); dst[di+1] = Byte(g); dst[di+2] = Byte(r); dst[di+3] = 255;
            }
        });
        return new PreviewResult(dst, w, h);
    }

    private static void Basic(ref double r, ref double g, ref double b, double exposure, double contrast, double temp, double tint, double sat, double vib, double highlights, double shadows, double whites, double blacks)
    {
        r*=exposure; g*=exposure; b*=exposure;
        r=(r-.5)*contrast+.5; g=(g-.5)*contrast+.5; b=(b-.5)*contrast+.5;
        double l=.2126*r+.7152*g+.0722*b;
        double sm=Math.Clamp(1-l*2,0,1), hm=Math.Clamp((l-.5)*2,0,1), wm=Math.Clamp((l-.7)/.3,0,1), bm=Math.Clamp((.3-l)/.3,0,1);
        double tone=shadows*sm*.35+highlights*hm*.25+whites*wm*.25+blacks*bm*-.25;
        r+=tone; g+=tone; b+=tone; r+=temp; b-=temp; r+=tint*.03; g-=tint*.03;
        double gray=(r+g+b)/3, vf=1+vib*(1-Math.Abs(gray-.5)*2);
        r=gray+(r-gray)*sat*vf; g=gray+(g-gray)*sat*vf; b=gray+(b-gray)*sat*vf;
    }

    private static void Curve(ref double r, ref double g, ref double b, Dictionary<string, Point[]> c)
    {
        r=Sample(c["R"],r); g=Sample(c["G"],g); b=Sample(c["B"],b);
        double l=.2126*r+.7152*g+.0722*b, m=Sample(c["L"],l), scale=l<.0001?1:m/l; r*=scale; g*=scale; b*=scale;
    }

    private static double Sample(Point[] p,double x)
    {
        x=Math.Clamp(x,0,1); for(int i=1;i<p.Length;i++) if(x<=p[i].X){double t=(x-p[i-1].X)/Math.Max(.0001,p[i].X-p[i-1].X);return Math.Clamp(p[i-1].Y+(p[i].Y-p[i-1].Y)*t,0,1);} return p[^1].Y;
    }

    private static void Grade(ref double r, ref double g, ref double b, Snapshot s)
    {
        if(s.GradeSat<.001&&Math.Abs(s.GradeLuma)<.001)return;
        double l=.2126*r+.7152*g+.0722*b, weight=s.GradeRange=="S"?Math.Clamp((.5-l)*2+.2,0,1):s.GradeRange=="H"?Math.Clamp((l-.5)*2+.2,0,1):1-Math.Abs(l-.5)*1.5;
        weight*=Math.Clamp(s.GradeBlend,0,1); double[] rgb=Hsv(s.GradeHue,s.GradeSat*weight,s.GradeLuma*.12*weight); r+=rgb[0];g+=rgb[1];b+=rgb[2];
    }

    private static double[] Hsv(double h,double s,double v)
    {
        h=((h%360)+360)%360; double c=Math.Abs(v)*s,x=c*(1-Math.Abs((h/60%2)-1)),m=v>=0?0:v,rr=0,gg=0,bb=0;
        if(h<60){rr=c;gg=x;}else if(h<120){rr=x;gg=c;}else if(h<180){gg=c;bb=x;}else if(h<240){gg=x;bb=c;}else if(h<300){rr=x;bb=c;}else{rr=c;bb=x;} return new[]{rr+m,gg+m,bb+m};
    }

    private static void DetailEffects(ref double r,ref double g,ref double b,byte[] src,int sw,int sh,int x,int y,Snapshot s)
    {
        double l=.2126*r+.7152*g+.0722*b;
        if(s.Sharpening!=0||s.NoiseReduction>0||s.Clarity!=0||s.Texture!=0){int px=Math.Max(0,x-1),nx=Math.Min(sw-1,x+1),py=Math.Max(0,y-1),ny=Math.Min(sh-1,y+1);double avg=0;foreach(var yy in new[]{py,y,ny})foreach(var xx in new[]{px,x,nx}){int i=(yy*sw+xx)*4;avg+=(.2126*src[i+2]+.7152*src[i+1]+.0722*src[i])/255.0;}avg/=9;double detail=l-avg, local=s.Clarity/100+s.Texture/150, nr=Math.Clamp(s.NoiseReduction/100,0,1), sharp=1+s.Sharpening/80; r=avg+(r-avg)*(1+local);g=avg+(g-avg)*(1+local);b=avg+(b-avg)*(1+local);r-=detail*nr;g-=detail*nr;b-=detail*nr;r=avg+(r-avg)*sharp;g=avg+(g-avg)*sharp;b=avg+(b-avg)*sharp;}
        if(s.DeHaze!=0){double d=s.DeHaze/100;r=(r-.5)*(1+d*.55)+.5;g=(g-.5)*(1+d*.55)+.5;b=(b-.5)*(1+d*.55)+.5;}
        if(s.Vignette!=0){double cx=x/(double)Math.Max(1,sw-1)-.5,cy=y/(double)Math.Max(1,sh-1)-.5,f=1+s.Vignette/100*(Math.Clamp(1-Math.Sqrt(cx*cx+cy*cy)*1.55,0,1)-1)*.8;r*=f;g*=f;b*=f;}
        if(s.Glow>0){double q=Math.Max(0,l-.62)*s.Glow/100;r+=q;g+=q;b+=q;}
        if(s.Halation>0){double q=Math.Max(0,l-.72)*s.Halation/100;r+=q*.18;g-=q*.025;b-=q*.02;}
        if(s.Grain>0){uint n=(uint)(x*374761393+y*668265263);n=(n^(n>>13))*1274126177u;double q=(((n^(n>>16))&1023)/1023.0-.5)*s.Grain/100*.12;r+=q;g+=q;b+=q;}
    }

    private static void Mask(ref double r,ref double g,ref double b,int x,int y,int w,int h,Snapshot s)
    {
        double strength=0;
        if(s.Brush&&s.MaskPoints.Length>0){double radius=Math.Max(2,s.MaskSize*Math.Min(w/(double)s.SourceWidth,h/(double)s.SourceHeight));foreach(var p in s.MaskPoints){double px=p.X/(s.SourceWidth-1)*(w-1),py=p.Y/(s.SourceHeight-1)*(h-1),d=Math.Sqrt((x-px)*(x-px)+(y-py)*(y-py));if(d<radius)strength=Math.Max(strength,1-d/radius);}}
        if(s.Linear){double dx=s.MaskEnd.X-s.MaskStart.X,dy=s.MaskEnd.Y-s.MaskStart.Y,len=Math.Max(.001,dx*dx+dy*dy),t=((x-s.MaskStart.X)*dx+(y-s.MaskStart.Y)*dy)/len;strength=Math.Max(strength,Math.Clamp(t,0,1));}
        if(s.Radial){double d=Math.Sqrt(Math.Pow(x/(double)w-s.MaskStart.X,2)+Math.Pow(y/(double)h-s.MaskStart.Y,2));strength=Math.Max(strength,Math.Clamp(1-d/Math.Max(.01,s.RadialRadius),0,1));}
        if(s.AutoSky)strength=Math.Max(strength,Math.Clamp(1-y/(double)Math.Max(1,h-1),0,1));
        if(s.AutoSubject){double cx=x/(double)Math.Max(1,w-1)-.5,cy=y/(double)Math.Max(1,h-1)-.5;strength=Math.Max(strength,Math.Clamp(1-Math.Sqrt(cx*cx+cy*cy)*2.1,0,1)*.7);}
        if(strength>.001){double f=Math.Pow(2,s.MaskExposure*strength);r*=f;g*=f;b*=f;}
    }

    private static byte Byte(double v)=>(byte)(Math.Clamp(v,0,1)*255+.5);

    private void CurveMouseDown(object sender,MouseButtonEventArgs e){_draggingCurve=true;CurveCanvas.CaptureMouse();MoveCurve(e.GetPosition(CurveCanvas));}
    private void CurveMouseMove(object sender,MouseEventArgs e){if(_draggingCurve)MoveCurve(e.GetPosition(CurveCanvas));}
    private void CurveMouseUp(object sender,MouseButtonEventArgs e){_draggingCurve=false;CurveCanvas.ReleaseMouseCapture();StartFastRender();}
    private void MoveCurve(Point p){double w=Math.Max(1,CurveCanvas.ActualWidth),h=Math.Max(1,CurveCanvas.ActualHeight),x=Math.Clamp(p.X/w,0,1),y=Math.Clamp(1-p.Y/h,0,1);var list=_curves[_curveChannel];int hit=-1;double best=.04;for(int i=1;i<list.Count-1;i++){double d=Math.Sqrt(Math.Pow(list[i].X-x,2)+Math.Pow(list[i].Y-y,2));if(d<best){best=d;hit=i;}}if(hit>=0)list[hit]=new Point(x,y);else list.Add(new Point(x,y));list.Sort((a,b)=>a.X.CompareTo(b.X));DrawCurve();_fastPreviewTimer.Stop();_fastPreviewTimer.Start();}
    private void CurveChannel_Click(object sender,RoutedEventArgs e){if(sender is Button b&&b.Tag is string t)_curveChannel=t;DrawCurve();}
    private void CurveReset_Click(object sender,RoutedEventArgs e){_curves[_curveChannel]=new(){new Point(0,0),new Point(1,1)};DrawCurve();StartFastRender();}
    private void DrawCurve(){if(CurveCanvas==null)return;CurveCanvas.Children.Clear();double w=Math.Max(1,CurveCanvas.ActualWidth),h=Math.Max(1,CurveCanvas.ActualHeight);for(int i=1;i<4;i++){CurveCanvas.Children.Add(new Line{X1=i*w/4,X2=i*w/4,Y1=0,Y2=h,Stroke=new SolidColorBrush(Color.FromRgb(55,55,55))});CurveCanvas.Children.Add(new Line{X1=0,X2=w,Y1=i*h/4,Y2=i*h/4,Stroke=new SolidColorBrush(Color.FromRgb(55,55,55))});}var line=new Polyline{Stroke=Brushes.White,StrokeThickness=2};foreach(var p in _curves[_curveChannel])line.Points.Add(new Point(p.X*w,(1-p.Y)*h));CurveCanvas.Children.Add(line);foreach(var p in _curves[_curveChannel]){var dot=new Ellipse{Width=8,Height=8,Fill=Brushes.White,Stroke=Brushes.Black};Canvas.SetLeft(dot,p.X*w-4);Canvas.SetTop(dot,(1-p.Y)*h-4);CurveCanvas.Children.Add(dot);}}

    private void GradeRange_Click(object sender,RoutedEventArgs e){if(sender is Button b&&b.Tag is string t)_gradeRange=t;}
    private void ColorWheelDown(object sender,MouseButtonEventArgs e){_draggingWheel=true;ColorWheelCanvas.CaptureMouse();MoveWheel(e.GetPosition(ColorWheelCanvas));}
    private void ColorWheelMove(object sender,MouseEventArgs e){if(_draggingWheel)MoveWheel(e.GetPosition(ColorWheelCanvas));}
    private void ColorWheelUp(object sender,MouseButtonEventArgs e){_draggingWheel=false;ColorWheelCanvas.ReleaseMouseCapture();StartFastRender();}
    private void MoveWheel(Point p){double cx=ColorWheelCanvas.ActualWidth/2,cy=ColorWheelCanvas.ActualHeight/2,dx=p.X-cx,dy=p.Y-cy,r=Math.Max(1,Math.Min(cx,cy));_gradeHue=(Math.Atan2(dy,dx)*180/Math.PI+360)%360;_gradeSat=Math.Clamp(Math.Sqrt(dx*dx+dy*dy)/r,0,1);_gradeLuma=Math.Clamp(1-Math.Sqrt(dx*dx+dy*dy)/r,0,1);DrawColorWheel();_fastPreviewTimer.Stop();_fastPreviewTimer.Start();}
    private void DrawColorWheel(){if(ColorWheelCanvas==null)return;ColorWheelCanvas.Children.Clear();double size=Math.Min(Math.Max(120,ColorWheelCanvas.ActualWidth),Math.Max(120,ColorWheelCanvas.ActualHeight)),cx=size/2,cy=size/2,r=size*.43;for(int i=0;i<24;i++){double a0=i*15-90,a1=(i+1)*15-90;var geo=new StreamGeometry();using(var c=geo.Open()){c.BeginFigure(new Point(cx,cy),true,true);c.LineTo(new Point(cx+Math.Cos(a0*Math.PI/180)*r,cy+Math.Sin(a0*Math.PI/180)*r),true,false);c.ArcTo(new Point(cx+Math.Cos(a1*Math.PI/180)*r,cy+Math.Sin(a1*Math.PI/180)*r),new Size(r,r),15,false,SweepDirection.Clockwise,true,false);}geo.Freeze();ColorWheelCanvas.Children.Add(new System.Windows.Shapes.Path{Data=geo,Fill=new SolidColorBrush(HsvColor(i*15,1,.8))});}var puck=new Ellipse{Width=12,Height=12,Fill=Brushes.White,Stroke=Brushes.Black};double pr=r*_gradeSat,pa=_gradeHue*Math.PI/180;Canvas.SetLeft(puck,cx+Math.Cos(pa)*pr-6);Canvas.SetTop(puck,cy+Math.Sin(pa)*pr-6);ColorWheelCanvas.Children.Add(puck);}
    private static Color HsvColor(double h,double s,double v){double c=v*s,x=c*(1-Math.Abs((h/60%2)-1)),m=v-c,rr=0,gg=0,bb=0;if(h<60){rr=c;gg=x;}else if(h<120){rr=x;gg=c;}else if(h<180){gg=c;bb=x;}else if(h<240){gg=x;bb=c;}else if(h<300){rr=x;bb=c;}else{rr=c;bb=x;}return Color.FromRgb((byte)((rr+m)*255),(byte)((gg+m)*255),(byte)((bb+m)*255));}

    private void MaskBrush_Click(object sender,RoutedEventArgs e){_brushMaskEnabled=true;_linearMaskEnabled=_radialMaskEnabled=_autoSkyMaskEnabled=_autoSubjectMaskEnabled=false;EnterTool(ToolMode.Mask);StatusText.Text="Aetherlight • Brush mask: paint on image";}
    private void MaskLinear_Click(object sender,RoutedEventArgs e){_linearMaskEnabled=true;_brushMaskEnabled=_radialMaskEnabled=_autoSkyMaskEnabled=_autoSubjectMaskEnabled=false;_toolMode=ToolMode.None;StatusText.Text="Aetherlight • Linear mask: drag on image";}
    private void MaskRadial_Click(object sender,RoutedEventArgs e){_radialMaskEnabled=true;_brushMaskEnabled=_linearMaskEnabled=_autoSkyMaskEnabled=_autoSubjectMaskEnabled=false;_toolMode=ToolMode.None;StatusText.Text="Aetherlight • Radial mask: drag on image";}
    private void MaskSky_Click(object sender,RoutedEventArgs e){_autoSkyMaskEnabled=true;_brushMaskEnabled=_linearMaskEnabled=_radialMaskEnabled=_autoSubjectMaskEnabled=false;_toolMode=ToolMode.None;StartFastRender();StatusText.Text="Aetherlight • Auto Sky mask";}
    private void MaskSubject_Click(object sender,RoutedEventArgs e){_autoSubjectMaskEnabled=true;_brushMaskEnabled=_linearMaskEnabled=_radialMaskEnabled=_autoSkyMaskEnabled=false;_toolMode=ToolMode.None;StartFastRender();StatusText.Text="Aetherlight • Auto Subject mask";}
    private void AdvancedPreviewDown(object sender,MouseButtonEventArgs e){if(!_linearMaskEnabled&&!_radialMaskEnabled)return;_draggingAdvancedMask=true;DevelopPreview.CaptureMouse();_maskDragStart=e.GetPosition(DevelopPreview);_maskDragEnd=_maskDragStart;_radialRadius=.01;DrawAdvancedMaskOverlay();e.Handled=true;}
    private void AdvancedPreviewMove(object sender,MouseEventArgs e){if(!_draggingAdvancedMask)return;_maskDragEnd=e.GetPosition(DevelopPreview);if(_radialMaskEnabled){double dx=_maskDragEnd.X-_maskDragStart.X,dy=_maskDragEnd.Y-_maskDragStart.Y;_radialRadius=Math.Clamp(Math.Sqrt(dx*dx+dy*dy)/Math.Max(1,Math.Min(DevelopPreview.ActualWidth,DevelopPreview.ActualHeight)),.01,1);}DrawAdvancedMaskOverlay();_fastPreviewTimer.Stop();_fastPreviewTimer.Start();}
    private void AdvancedPreviewUp(object sender,MouseButtonEventArgs e){if(!_draggingAdvancedMask)return;_draggingAdvancedMask=false;DevelopPreview.ReleaseMouseCapture();StartFastRender();e.Handled=true;}
    private void DrawAdvancedMaskOverlay(){DrawMaskOverlay();if(OverlayCanvas==null)return;if(_linearMaskEnabled)OverlayCanvas.Children.Add(new Line{X1=_maskDragStart.X,Y1=_maskDragStart.Y,X2=_maskDragEnd.X,Y2=_maskDragEnd.Y,Stroke=Brushes.White,StrokeThickness=2,StrokeDashArray=new DoubleCollection{4,3}});if(_radialMaskEnabled){double rr=_radialRadius*Math.Min(DevelopPreview.ActualWidth,DevelopPreview.ActualHeight);var el=new Ellipse{Width=rr*2,Height=rr*2,Stroke=Brushes.White,StrokeThickness=2,Fill=Brushes.Transparent};Canvas.SetLeft(el,_maskDragStart.X-rr);Canvas.SetTop(el,_maskDragStart.Y-rr);OverlayCanvas.Children.Add(el);}}

    private void ResetAdvanced_Click(object sender,RoutedEventArgs e){_loading=true;SharpeningSlider.Value=NoiseReductionSlider.Value=ClaritySlider.Value=TextureSlider.Value=DehazeSlider.Value=VignetteSlider.Value=GrainSlider.Value=GlowSlider.Value=HalationSlider.Value=0;GradeBlendSlider.Value=1;GradeBalanceSlider.Value=0;_loading=false;_gradeHue=_gradeSat=_gradeLuma=0;_gradeRange="M";foreach(var k in _curves.Keys.ToList())_curves[k]=new(){new Point(0,0),new Point(1,1)};_brushMaskEnabled=_linearMaskEnabled=_radialMaskEnabled=_autoSkyMaskEnabled=_autoSubjectMaskEnabled=false;_maskPoints.Clear();DrawCurve();DrawColorWheel();DrawMaskOverlay();StartFastRender();}

    private void ExportAdvanced_Click(object sender,RoutedEventArgs e){if(_originalPixels==null){MessageBox.Show("Import and select a photo first.","Aetherlight",MessageBoxButton.OK,MessageBoxImage.Information);return;}StatusText.Text="Aetherlight • Rendering full resolution…";RenderFullAdvanced();Export_Click(sender,e);StatusText.Text="Aetherlight • Ready";}
    private void RenderFullAdvanced(){ApplyAdjustments();if(_editedBitmap==null)return;byte[] src=new byte[_pixelWidth*_pixelHeight*4];_editedBitmap.CopyPixels(src,_pixelWidth*4,0);byte[] dst=(byte[])src.Clone();Snapshot s=SnapshotState(_pixelWidth,_pixelHeight);Parallel.For(0,_pixelHeight,y=>{for(int x=0;x<_pixelWidth;x++){int i=(y*_pixelWidth+x)*4;double b=src[i]/255.0,g=src[i+1]/255.0,r=src[i+2]/255.0;Curve(ref r,ref g,ref b,s.Curves);Grade(ref r,ref g,ref b,s);DetailEffects(ref r,ref g,ref b,src,_pixelWidth,_pixelHeight,x,y,s);Mask(ref r,ref g,ref b,x,y,_pixelWidth,_pixelHeight,s);dst[i]=Byte(b);dst[i+1]=Byte(g);dst[i+2]=Byte(r);dst[i+3]=255;}});var bmp=BitmapSource.Create(_pixelWidth,_pixelHeight,96,96,PixelFormats.Bgra32,null,dst,_pixelWidth*4);bmp.Freeze();_editedBitmap=new WriteableBitmap(bmp);_editedBitmap.Freeze();DevelopPreview.Source=_editedBitmap;}

    private readonly record struct PreviewResult(byte[] Pixels,int Width,int Height);
    private readonly record struct Snapshot(double Sharpening,double NoiseReduction,double Clarity,double Texture,double DeHaze,double Vignette,double Grain,double Glow,double Halation,double GradeHue,double GradeSat,double GradeLuma,double GradeBlend,double GradeBalance,string GradeRange,Dictionary<string,Point[]> Curves,Point[] MaskPoints,bool Brush,bool Linear,bool Radial,bool AutoSky,bool AutoSubject,Point MaskStart,Point MaskEnd,double RadialRadius,double MaskSize,double MaskExposure,int SourceWidth,int SourceHeight);
}
