using System.Numerics;

namespace Lotus.Infrastructure.Score;

// Actual CPU-rendered petals, not a decorative GPU preview running alongside a CPU measurement.
internal static class LotusCpuScene
{
    private readonly record struct Petal(Vector3 Center, Vector3 Right, Vector3 Up, Vector3 Normal, Vector3 Radius);
    private static readonly Petal[] Petals = Build();
    private static readonly Vector3 Light = Vector3.Normalize(new(-0.6f,1,-0.4f));
    private static Petal[] Build()
    {
        var petals=new List<Petal>();
        for(var layer=0;layer<2;layer++)
        for(var i=0;i<(layer==0?9:7);i++)
        {
            var angle=i*MathF.Tau/(layer==0?9:7)+layer*0.35f;
            var radial=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
            var right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,radial));
            var up=Vector3.Normalize(radial*(layer==0?0.8f:0.3f)+Vector3.UnitY);
            petals.Add(new(new Vector3(0,0.1f+layer*0.28f,4.5f)+radial*(layer==0?0.78f:0.25f),right,up,
                Vector3.Cross(right,up),layer==0?new(0.35f,1.0f,0.13f):new(0.29f,0.86f,0.11f)));
        }
        return [..petals];
    }
    public static Vector3 Pixel(int x,int y,int frame)
    {
        var eye=new Vector3(0.18f*MathF.Sin(frame/90f),1.1f,-1);
        var color=Vector3.Zero;
        for(var sample=0;sample<4;sample++)
        {
            var u=(x+(sample&1)*0.5f)/320*2-1;
            var v=1-(y+(sample>>1)*0.5f)/180*2;
            color+=Trace(eye,Vector3.Normalize(new(u*1.78f,v-0.3f,2.1f)),0);
        }
        return color/4;
    }
    private static Vector3 Trace(Vector3 origin,Vector3 dir,int depth)
    {
        var (hit,t)=Hit(origin,dir);
        if(hit==-1)
        {
            var star=MathF.Sin(dir.X*4127+dir.Y*1793)*43758.5f;
            return new Vector3(0.006f,0.008f,0.023f)+new Vector3(0.06f,0.013f,0.07f)*MathF.Exp(-dir.Y*dir.Y*8)
                +new Vector3(star-MathF.Floor(star)>0.998f?0.4f:0);
        }
        var p=origin+dir*t; var n=Vector3.UnitY;
        if(hit>=0)
        {
            var petal=Petals[hit]; var delta=p-petal.Center;
            n=Vector3.Normalize(petal.Right*(Vector3.Dot(delta,petal.Right)/(petal.Radius.X*petal.Radius.X))
                +petal.Up*(Vector3.Dot(delta,petal.Up)/(petal.Radius.Y*petal.Radius.Y))
                +petal.Normal*(Vector3.Dot(delta,petal.Normal)/(petal.Radius.Z*petal.Radius.Z)));
        }
        var shadow=Hit(p+n*1e-3f,Light).Index!=-1?0.25f:1f;
        var diffuse=Math.Max(0,Vector3.Dot(n,Light))*shadow;
        var spec=MathF.Pow(Math.Max(0,Vector3.Dot(Vector3.Reflect(-Light,n),-dir)),32)*shadow;
        var local=(hit>=0?new Vector3(0.91f,0.53f,0.23f):new Vector3(0.025f,0.009f,0.032f))*(0.22f+0.78f*diffuse)+new Vector3(spec*0.6f);
        if(hit>=0)local+=new Vector3(0.06f,0.016f,0.004f);
        if(depth>=3)return local;
        return Vector3.Lerp(local,Trace(p+n*1e-3f,Vector3.Reflect(dir,n),depth+1),hit>=0?0.22f:0.38f);
    }
    private static (int Index,float T) Hit(Vector3 origin,Vector3 direction)
    {
        var floor=direction.Y < -1e-5f?(-1-origin.Y)/direction.Y:-1;
        var best=floor>1e-4f?floor:float.MaxValue; var index=floor>1e-4f?-2:-1;
        for(var i=0;i<Petals.Length;i++)
        {
            var p=Petals[i]; var delta=origin-p.Center;
            var q=new Vector3(Vector3.Dot(delta,p.Right),Vector3.Dot(delta,p.Up),Vector3.Dot(delta,p.Normal))/p.Radius;
            var ray=new Vector3(Vector3.Dot(direction,p.Right),Vector3.Dot(direction,p.Up),Vector3.Dot(direction,p.Normal))/p.Radius;
            var a=Vector3.Dot(ray,ray); var b=Vector3.Dot(q,ray); var c=Vector3.Dot(q,q)-1;
            var h=b*b-a*c; if(h<0)continue;
            var t=(-b-MathF.Sqrt(h))/a;
            if(t>1e-4f&&t<best){best=t;index=i;}
        }
        return (index,best);
    }
}
