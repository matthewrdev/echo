namespace Echo.Tests;

internal sealed class TestSuite
{
    private int passed;
    private int failed;
    public void Run(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            failed++;
            Console.Error.WriteLine("FAIL " + name + "\n" + e);
        }
    }

    public int Finish()
    {
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
