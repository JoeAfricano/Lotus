using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lotus.Infrastructure.Games;

namespace Lotus.Infrastructure.Integrations;

// A sample-data preview of the actual .ovl, not an independently drawn miniature. RTSS remains the final renderer.
[SupportedOSPlatform("windows")]
public static class RtssLayoutPreview
{
    public sealed record Frame(byte[] Png,int X,int Y,int Width,int Height,string Key);
    static readonly ConcurrentDictionary<string,Lazy<Frame>> Cache=new();
    static readonly object DrawGate=new();
    static readonly Lazy<ImageCodecInfo> PngEncoder=new(()=>ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Png.Guid));
    static readonly Dictionary<string,string> Samples=new(StringComparer.Ordinal)
    {
        ["CPU Load"]="38",["CPU Temp"]="58",["CPU Clock"]="4.2",["GPU Load"]="71",["GPU Temp"]="64",["GPU Clock"]="2730",
        ["VRAM Load"]="59",["VRAM Used"]="4.7",["VRAM Total"]="8",["RAM Load"]="61",["RAM Used"]="9.8",["RAM Total"]="16",
        ["FPS Avg Live"]="142",["FPS Low1 Live"]="118",["FPS Low01 Live"]="96",["Lotus Pulse"]="0.2",
        ["Lotus Latency"]="46",["Lotus Latency Ok"]="1",["Lotus Latency Missing"]="0"
    };
    public static Frame Render(string variant)
    {
        if(!LotusOverlayLayout.Variants.Contains(variant)||variant==LotusOverlayLayout.Window)throw new ArgumentException("Unsupported native preview.",nameof(variant));
        string text=LotusOverlayLayout.Text(variant);
        string key=Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(text)));
        if(Cache.Count>32)Cache.Clear();
        var pending=Cache.GetOrAdd(key,_=>new Lazy<Frame>(()=>{lock(DrawGate)return Draw(text,key);}));
        try{return pending.Value;}
        catch {Cache.TryRemove(new KeyValuePair<string,Lazy<Frame>>(key,pending));throw;}
    }
    record Layer(string Text,int X,int Y,int W,int H,float FontSize,Color Color);
    static Frame Draw(string text,string key)
    {
        var ini=new IniText(text);
        int Int(string section,string name,int fallback)=>int.TryParse(ini.Get(section,name),out var n)?n:fallback;
        float fontHeight=Math.Abs(Int("Master","FontHeight",-16));
        var layers=new List<Layer>();
        int count=Int("General","Layers",0);
        if(count is <1 or >512)throw new InvalidDataException("Invalid preview layer count.");
        for(int i=0;i<count;i++)
        {
            string s="Layer"+i,content=ini.Get(s,"Text")??"";
            string color=(ini.Get(s,"TextColor")??"FFFFFF").Split(',')[0];
            if(color.Length==6)color="FF"+color;
            var c=uint.TryParse(color,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgba)?Color.FromArgb(unchecked((int)rgba)):Color.White;
            if(c.A==0||content.Length==0)continue;
            if(ini.Get(s,"VisibilitySource") is {Length:>0} visibility && Samples.TryGetValue(visibility,out var shown) && shown=="0")continue; // hidden with the sample data
            int x=Int(s,"PositionX",0),y=Int(s,"PositionY",0),w=Int(s,"ExtentX",0),h=Int(s,"ExtentY",0);
            float size=fontHeight*Int(s,"Size",100)/100f;
            if(Math.Abs(x)>8192||Math.Abs(y)>8192||w>8192||h>8192||size is <=0 or >512)throw new InvalidDataException("Invalid preview geometry.");
            layers.Add(new(content,x,y,w,h,size,c));
        }
        if(layers.Count==0)throw new InvalidDataException("Empty preview.");
        int left=layers.Min(l=>l.X),top=layers.Min(l=>l.Y);
        int right=layers.Max(l=>l.X+Math.Max(1,l.W)),bottom=layers.Max(l=>l.Y+Math.Max(1,l.H));
        int width=right-left,height=bottom-top;
        if(width is <1 or >8192||height is <1 or >4096)throw new InvalidDataException("Invalid preview bounds.");
        using var bitmap=new Bitmap(width*2,height*2,PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(bitmap);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.ScaleTransform(2,2);g.TranslateTransform(-left,-top);
        using var artStream=new MemoryStream(LotusOverlayLayout.PreviewAsset(ini.Get("Settings","EmbeddedImage")??LotusOverlayLayout.ArtFile));
        using var art=Image.FromStream(artStream);
        foreach(var l in layers)
        {
            using var brush=new SolidBrush(l.Color);
            if(l.Text.StartsWith("<I="))
            {
                var p=Parts(l.Text);if(p.Length<6)throw new InvalidDataException("Invalid sprite.");
                g.DrawImage(art,new RectangleF(l.X,l.Y,p[0],p[1]),new RectangleF(p[2],p[3],p[4],p[5]),GraphicsUnit.Pixel);
            }
            else if(l.Text.StartsWith("<B="))
            {
                var p=Parts(l.Text);float radius=l.Text.Contains(",R")?Math.Min(p[0],p[1])*.1f:0;
                if(radius>0){using var path=Rounded(l.X,l.Y,p[0],p[1],radius);g.FillPath(brush,path);}else g.FillRectangle(brush,l.X,l.Y,p[0],p[1]);
            }
            else if(l.Text.StartsWith("<G="))
            {
                var p=l.Text[3..l.Text.IndexOf('>')].Split(',');float w=l.W,h=l.H;
                if(p.Length>2){if(float.Parse(p[1],CultureInfo.InvariantCulture)>0)w=float.Parse(p[1],CultureInfo.InvariantCulture);if(float.Parse(p[2],CultureInfo.InvariantCulture)>0)h=float.Parse(p[2],CultureInfo.InvariantCulture);}
                if(p[0]!="Frametime")
                {
                    float value=Samples.TryGetValue(p[0],out var sample)?float.Parse(sample,CultureInfo.InvariantCulture)/100:0;
                    g.FillRectangle(brush,l.X,l.Y,w*value,h);
                }
                else
                {
                    float max=p.Length>5?float.Parse(p[5],CultureInfo.InvariantCulture):33.3f;
                    var points=Enumerable.Range(0,100).Select(i=>new PointF(l.X+w*i/99,l.Y+h*(1-(6.9f+(i==34?8:i==76?4:i%11==0?.8f:0))/max))).ToArray();
                    using var pen=new Pen(l.Color,1);g.DrawLines(pen,points);
                }
            }
            else
            {
                string value=l.Text.Replace("<FR>","144").Replace("<FT>","6.9").Replace("<API>","D3D12").Replace("<RES>","1920x1080");
                value=Regex.Replace(value,@"%([A-Za-z][^%\r\n]*?)%",m=>Samples.TryGetValue(m.Groups[1].Value,out var sample)?sample:"--");
                if(value.Contains('<'))throw new InvalidDataException("Unsupported preview tag: "+value);
                using var font=new Font("Segoe UI Semibold",l.FontSize,FontStyle.Regular,GraphicsUnit.Pixel);
                using var format=(StringFormat)StringFormat.GenericTypographic.Clone();format.FormatFlags|=StringFormatFlags.NoClip;
                g.DrawString(value,font,brush,new PointF(l.X,l.Y),format);
            }
        }
        using var png=new MemoryStream();bitmap.Save(png,PngEncoder.Value,null);return new(png.ToArray(),left,top,width,height,key);
    }
    static float[] Parts(string tag)=>tag[(tag.IndexOf('=')+1)..tag.IndexOf('>')].Split(',').TakeWhile(p=>!p.StartsWith('R')).Select(p=>float.Parse(p,CultureInfo.InvariantCulture)).ToArray();
    static GraphicsPath Rounded(float x,float y,float w,float h,float r)
    {
        var p=new GraphicsPath();float d=r*2;p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();return p;
    }
}
