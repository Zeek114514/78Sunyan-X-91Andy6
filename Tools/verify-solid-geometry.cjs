// Exercises the actual Cylinder/Prism C# methods without starting another Unity editor.
const fs = require('fs');
const source = fs.readFileSync('Assets/Scripts/LowPoly.cs', 'utf8');
function method(name) {
  const start = source.indexOf('    public static void ' + name + '(');
  const end = source.indexOf('\n    /// <summary>', start);
  if (start < 0 || end < 0) throw Error('Missing geometry method: ' + name);
  return source.slice(start, end);
}
const fixture = `using System; using System.Collections.Generic;
struct Vector3 {
 public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public static Vector3 up => new Vector3(0,1,0);
 public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator *(Vector3 a,float k)=>new Vector3(a.x*k,a.y*k,a.z*k);
 public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
}
struct Color {public Color linear=>this;}
static class Mathf { public const float PI=(float)Math.PI; public static float Sin(float x)=>(float)Math.Sin(x); public static float Cos(float x)=>(float)Math.Cos(x); }
static class Geometry {
${method('Cylinder')}
${method('Prism')}
}
static class Check {
 static void Main(){int tests=0;
 foreach(int sides in new[]{4,8,10,16}) foreach(float top in new[]{.02f,.8f,1.5f}) {
  var v=new List<Vector3>();var c=new List<Color>();var t=new List<int>();
  Geometry.Prism(v,c,t,new Vector3(0,0,0),3,1.5f,top,sides,new Color());
  if(t.Count!=sides*12)throw new Exception("Solid triangle count");
  int tops=0,bottoms=0;
  for(int i=0;i<t.Count;i+=3){var a=v[t[i]];var b=v[t[i+1]];var d=v[t[i+2]];var n=Vector3.Cross(b-a,d-a);
   if(a.y==3&&b.y==3&&d.y==3){if(n.y<=0)throw new Exception("Inverted top");tops++;}
   else if(a.y==0&&b.y==0&&d.y==0){if(n.y>=0)throw new Exception("Inverted bottom");bottoms++;}
   else if(Vector3.Dot(n,new Vector3(a.x+b.x+d.x,0,a.z+b.z+d.z))<=0)throw new Exception("Inverted side");
  }
  if(tops!=sides||bottoms!=sides)throw new Exception("Missing cap");tests++;
 }
 Console.WriteLine("PASS: "+tests+" actual C# geometry cases; outward sides, upward tops, downward bottoms, closed counts. Not a Unity visual test."); }
}`;
fs.mkdirSync('Artifacts/Geometry', {recursive:true});
fs.writeFileSync('Artifacts/Geometry/Regression.cs', fixture);
