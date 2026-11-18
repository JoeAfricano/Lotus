using Lotus.Infrastructure.Integrations;
using System.Text.Json;
if(!OperatingSystem.IsWindows())return 1;
var folder=Path.GetFullPath(args.Length>0?args[0]:"artifacts/overlay-previews");Directory.CreateDirectory(folder);
foreach(var variant in LotusOverlayLayout.Variants.Where(x=>x!=LotusOverlayLayout.Window))
{
    var frame=RtssLayoutPreview.Render(variant);File.WriteAllBytes(Path.Combine(folder,variant+".png"),frame.Png);
    File.WriteAllText(Path.Combine(folder,variant+".json"),JsonSerializer.Serialize(new{variant,frame.X,frame.Y,frame.Width,frame.Height,frame.Key,exampleData=true,source="actual embedded OVL"}));
    Console.WriteLine($"{variant}: {frame.Width}x{frame.Height} at {frame.X},{frame.Y}");
}
return 0;
