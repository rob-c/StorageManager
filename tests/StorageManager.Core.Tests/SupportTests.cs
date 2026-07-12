using StorageManager;

namespace StorageManager.Core.Tests;

public class SupportTests
{
    [Fact]
    public void Support_line_names_the_contact()
    {
        Assert.Contains("Robert Currie", Support.Line);
        Assert.Contains("rob.currie@ed.ac.uk", Support.Line);
    }
}
