using NamePlate.Tests;

// WPF text and the editor control need an STA thread.
int result = 1;
var thread = new Thread(() =>
{
    CoreTests.Run();
    EdgeCaseTests.Run();
    ViewModelTests.Run();
    EditorTests.Run();
    HolderTests.Run();
    result = T.Summary();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return result;
