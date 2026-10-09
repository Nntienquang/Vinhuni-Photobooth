using Booth;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Bridge.Tests;
public sealed class StorageTests
{
    [Fact]public async Task OfflineQueueSurvivesRestartWithoutOverwritingPrintState()
    {
        var root=Path.Combine(Path.GetTempPath(),"booth-queue-"+Guid.NewGuid());var previous=Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR");var previousUrl=Environment.GetEnvironmentVariable("PHOTOBOOTH_CLOUD_URL");
        try{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",root);Environment.SetEnvironmentVariable("PHOTOBOOTH_CLOUD_URL",null);var store=new Storage();var s=new Session{Template=new("test","Test",600,900,300,1,"portrait",true,0,[new("1",0,0,600,900)]),Confirmed=true,FinalPath=Path.Combine(root,"final.jpg")};store.Save(s);var stale=store.Find(s.Id);s.PrintStatus="sent";store.Save(s);var cloud=new CloudQueue(store,NullLogger<CloudQueue>.Instance);await cloud.Upload(stale);var restarted=new Storage();var restored=restarted.Find(s.Id);Assert.Equal("pending",restored.UploadStatus);Assert.Equal("sent",restored.PrintStatus);Assert.True(File.Exists(Path.Combine(restarted.SessionDir(restored),"session.json")));restarted.UpdateUpload(s.Id,"uploaded","https://example.test/p/id");store.Save(s);Assert.Equal("uploaded",restarted.Find(s.Id).UploadStatus);}
        finally{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",previous);Environment.SetEnvironmentVariable("PHOTOBOOTH_CLOUD_URL",previousUrl);Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]public void TemplateImportRejectsTraversalAndOversizedMetadata()
    {
        var root=Path.Combine(Path.GetTempPath(),"booth-zip-"+Guid.NewGuid());var previous=Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR");
        try{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",root);var store=new Storage();var templates=new Templates(store);Assert.Throws<ArgumentException>(()=>templates.Dir("../escape"));using var stream=new MemoryStream();using(var zip=new System.IO.Compression.ZipArchive(stream,System.IO.Compression.ZipArchiveMode.Create,true)){using var writer=new StreamWriter(zip.CreateEntry("../escape.png").Open());writer.Write("bad");}stream.Position=0;Assert.Throws<ArgumentException>(()=>templates.Import(stream));templates.Seed();var original=templates.All()[0];using var package=new MemoryStream(templates.Export(original.Id));var imported=templates.Import(package);Assert.Equal(original.Slots,imported.Slots);Assert.NotEqual(original.Id,imported.Id);}
        finally{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",previous);Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]public void TemplateSaveCreatesOfflineThumbnail()
    {
        var root=Path.Combine(Path.GetTempPath(),"booth-template-"+Guid.NewGuid());var previous=Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR");
        try{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",root);var store=new Storage();var templates=new Templates(store);templates.Seed();var template=templates.All().First();Assert.True(File.Exists(templates.EnsureThumbnail(template.Id)));Assert.Equal("thumbnail.jpg",templates.Get(template.Id).ThumbnailPath);}
        finally{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",previous);Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Theory][InlineData(1024,1536)][InlineData(2000,3000)]public void TemplatePngBytesAndDimensionsArePreserved(int width,int height)
    {
        var root=Path.Combine(Path.GetTempPath(),"booth-template-normalize-"+Guid.NewGuid());var previous=Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR");
        try{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",root);var store=new Storage();var templates=new Templates(store);templates.Seed();var template=templates.All().First();var png=CreatePng(width,height);templates.Save(template,png);Assert.Equal(png,File.ReadAllBytes(Path.Combine(templates.Dir(template.Id),"frame.png")));using var image=SKBitmap.Decode(Path.Combine(templates.Dir(template.Id),"frame.png"));Assert.NotNull(image);Assert.Equal(width,image!.Width);Assert.Equal(height,image.Height);}
        finally{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",previous);Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]public void TemplatePngWithWrongAspectRatioIsRejected()
    {
        var root=Path.Combine(Path.GetTempPath(),"booth-template-aspect-"+Guid.NewGuid());var previous=Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR");
        try{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",root);var store=new Storage();var templates=new Templates(store);templates.Seed();var template=templates.All().First();var error=Assert.Throws<ArgumentException>(()=>templates.Save(template,CreatePng(1200,1200)));Assert.Contains("1200x1200",error.Message);Assert.Contains("1200:1800",error.Message);Assert.Contains("1200x1800",error.Message);}
        finally{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",previous);Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]public void TemplatePngPreservesAlphaTransparency()
    {
        var root=Path.Combine(Path.GetTempPath(),"booth-template-alpha-"+Guid.NewGuid());var previous=Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR");
        try{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",root);var store=new Storage();var templates=new Templates(store);templates.Seed();var template=templates.All().First();templates.Save(template,CreatePng(1024,1536,true));using var image=SKBitmap.Decode(Path.Combine(templates.Dir(template.Id),"frame.png"));Assert.NotNull(image);Assert.Equal(0,image!.GetPixel(0,0).Alpha);Assert.Equal(255,image.GetPixel(600,900).Alpha);}
        finally{Environment.SetEnvironmentVariable("PHOTOBOOTH_DATA_DIR",previous);Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static byte[] CreatePng(int width,int height,bool withAlpha=false)
    {
        using var bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Transparent);
        if(withAlpha){using var canvas=new SKCanvas(bitmap);canvas.DrawRect(width/4f,height/4f,width*3/4f,height*3/4f,new SKPaint{Color=SKColors.Red});}
        using var data=bitmap.Encode(SKEncodedImageFormat.Png,100);return data.ToArray();
    }
}
