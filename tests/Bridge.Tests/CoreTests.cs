using Booth;
using Booth.Camera;
using SkiaSharp;
using Xunit;
namespace Bridge.Tests;
public sealed class CoreTests
{
    static Template Template(int shots=1)=>new("test","Test",600,900,300,shots,"portrait",true,0,Enumerable.Range(0,shots).Select(i=>new Slot(i.ToString(),20,20+i*180,560,160,ShotIndex:i)).ToList());
    [Fact]public void CoverCropPreservesAspectAndCenters(){var r=PhotoComposer.Cover(2400,1600,500,600);Assert.Equal(1333.3333,r.Width,2);Assert.Equal(1600,r.Height);Assert.Equal(533.3333,r.Left,2);}
    [Fact]public void FocalPointClampsToPhoto(){var r=PhotoComposer.Cover(2400,1600,500,600,1,0);Assert.Equal(2400,r.Right);Assert.Equal(0,r.Top);}
    [Theory][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]public void VariableShotCountsValidate(int n)=>Templates.Validate(Template(n));
    [Fact]public void InvalidTemplateRejected(){var t=Template() with{Slots=[new Slot("1",590,0,30,300)]};Assert.Throws<ArgumentException>(()=>Templates.Validate(t));}
    [Fact]public void EveryShotMustBeUsed(){var t=Template(2) with{Slots=[new Slot("1",0,0,100,100),new Slot("2",100,0,100,100)]};Assert.Throws<ArgumentException>(()=>Templates.Validate(t));}
    [Fact]public void NonfiniteSlotRejected()=>Assert.Throws<ArgumentException>(()=>Templates.Validate(Template() with{Slots=[new Slot("1",float.NaN,0,100,100)]}));
    [Fact]public void NewTemplateDefaultsToSixCapturesAndFourSelections(){var t=Template(4) with{OutputWidth=1200,Slots=Enumerable.Range(0,4).Select(i=>new Slot(i.ToString(),20,20+i*200,500,160,ShotIndex:i)).ToList(),CaptureCount=6,SelectionCount=4,DuplicateStrip=true};Assert.Equal(6,t.EffectiveCaptureCount);Assert.Equal(4,t.EffectiveSelectionCount);Templates.Validate(t);Assert.Equal(8,Templates.ExpandedSlots(t).Count);}
    [Fact]public void DuplicateStripDoesNotMirrorPhotos(){var t=Template(1) with{OutputWidth=1200,Slots=[new Slot("left",50,50,500,500)]};var slots=Templates.ExpandedSlots(t with{DuplicateStrip=true});Assert.Equal(50,slots[0].X);Assert.Equal(650,slots[1].X);Assert.Equal(0,slots[0].ShotIndex);Assert.Equal(0,slots[1].ShotIndex);}
    [Fact]public void StateMachineRejectsCaptureFromPreview(){var s=new Session{Template=Template(),State=BoothState.FINAL_PREVIEW};Assert.Throws<InvalidOperationException>(()=>SessionRules.Require(s,BoothState.LIVE_VIEW,BoothState.READY));}
    [Fact]public void RetakeLimitAndFinalLock(){var s=new Session{Template=Template(),State=BoothState.FINAL_PREVIEW};SessionRules.Retake(s,new Settings());s.Retakes=1;Assert.Throws<InvalidOperationException>(()=>SessionRules.Retake(s,new Settings()));s.Retakes=0;s.Confirmed=true;Assert.Throws<InvalidOperationException>(()=>SessionRules.Retake(s,new Settings()));}
    [Theory][InlineData("sent")][InlineData("sending")][InlineData("uncertain")]public void DuplicatePrintPrevented(string status){var s=new Session{Template=Template(),Confirmed=true,FinalPath="final.jpg",PrintStatus=status};Assert.Throws<InvalidOperationException>(()=>SessionRules.Print(s));}
    [Fact]public void CountdownOnlyAllowsSupportedValues(){Assert.Throws<ArgumentException>(()=>(new Settings{Countdown=0}).Validate());foreach(var seconds in new[]{3,5,10})(new Settings{Countdown=seconds}).Validate();}
    [Fact]public async Task MockCapturesFullResolutionAndComposerDrawsOverlayLast()
    {
        var dir=Path.Combine(Path.GetTempPath(),"booth-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try{await using var mock=new MockCameraProvider();await mock.InitializeAsync();await mock.ConnectAsync();var path=await mock.CaptureAsync(Path.Combine(dir,"original.jpg"),CancellationToken.None);using(var original=SKBitmap.Decode(path)){Assert.Equal(2400,original.Width);Assert.Equal(1600,original.Height);}using(var overlay=new SKBitmap(600,900)){using var c=new SKCanvas(overlay);c.Clear(SKColors.Transparent);using var p=new SKPaint{Color=SKColors.Red};c.DrawRect(20,20,60,60,p);using var data=overlay.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(Path.Combine(dir,"frame.png"),data.ToArray());}var final=new PhotoComposer().Compose(Template(),[path],Path.Combine(dir,"frame.png"),dir,new Settings{ExportPng=true});using var result=SKBitmap.Decode(final);Assert.Equal(600,result.Width);Assert.Equal(900,result.Height);Assert.Equal(SKColors.Red,result.GetPixel(30,30));Assert.NotEqual(SKColors.Transparent,result.GetPixel(150,80));}
        finally{Directory.Delete(dir,true);}
    }
}
