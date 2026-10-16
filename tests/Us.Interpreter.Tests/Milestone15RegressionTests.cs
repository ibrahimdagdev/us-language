namespace Us.Interpreter.Tests;

public class InterpreterRegressionTests
{
    [Fact]
    public void HelloAdultSumAndModelKeepInterpreterOutput()
    {
        AssertInterpreter("syaz \"Merhaba\"\n", "Merhaba");
        AssertInterpreter("""
            yaş: tamsayı = 20
            durum: metin = eğer yaş >= 18 => "Yetişkin" !=> "Çocuk"
            syaz durum
            """.Replace("\r\n", "\n", StringComparison.Ordinal), "Yetişkin");
        AssertInterpreter("""
            fonksiyon topla(a: tamsayı, b: tamsayı): tamsayı
            {
                => a + b
            }
            sonuç: tamsayı = topla(2, 3)
            syaz "{sonuç}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal), "5");
        AssertInterpreter("""
            model Kişi
            {
                ad: metin
            }
            kişi: Kişi = @Kişi { ad = "Ali" }
            syaz kişi.ad
            """.Replace("\r\n", "\n", StringComparison.Ordinal), "Ali");
    }

    private static void AssertInterpreter(string source, string expectedLine)
    {
        var result = ProgramRunner.RunText("reg.us", source);
        Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.Contains(result.Output.Lines, line => line.Contains(expectedLine, StringComparison.Ordinal));
    }
}
