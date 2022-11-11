using NUnit.Framework;
using Riten.Rollback;

public class TestHistory
{
    struct TestData
    {
        public bool W, A, S, D;
    }

    readonly TestData inputA = new TestData{
        W = false,
        A = true,
        S = false,
        D = false
    };

    readonly TestData inputB = new TestData{
        W = true,
        A = false,
        S = false,
        D = false
    };

    readonly TestData inputC = new TestData{
        W = true,
        A = true,
        S = true,
        D = true
    };

    [Test]
    public void SimpleSearch()
    {
        History<TestData> history = new (3);

        history.Write(100, inputB);
        history.Write(0, inputA);
        history.Write(69, inputC);

        history.Find(100, out var bIndex);
        history.Find(0, out var aIndex);
        history.Find(69, out var cIndex);

        Assert.AreEqual(aIndex, 0);
        Assert.AreEqual(bIndex, 2);
        Assert.AreEqual(cIndex, 1);
    }

    [Test]
    public void SimpleOverrideData()
    {
        History<TestData> history = new (3);

        history.Write(0, inputB);
        history.Write(0, inputA);
        history.Write(0, inputC);

        history.Find(0, out var aIndex);

        Assert.AreEqual(aIndex, 0);
        Assert.AreEqual(inputC, history[aIndex]);
        Assert.AreNotEqual(inputA, history[aIndex]);
        Assert.AreNotEqual(inputB, history[aIndex]);
    }

    [Test]
    public void SimpleDirectAccess()
    {
        History<TestData> history = new (3);

        history.Write(100, inputB);
        history.Write(0, inputA);
        history.Write(69, inputC);

        Assert.AreEqual(inputA, history[0]);
        Assert.AreEqual(inputB, history[2]);
        Assert.AreEqual(inputC, history[1]);
    }

    [Test]
    public void SimpleRead()
    {
        History<TestData> history = new (3);

        history.Write(0, inputA);
        history.Write(1, inputB);
        history.Write(2, inputC);

        var readInputAValid = history.Read(0, out var readInputA);
        var readInputBValid = history.Read(1, out var readInputB);
        var readInputCValid = history.Read(2, out var readInputC);

        Assert.IsTrue(readInputAValid);
        Assert.IsTrue(readInputBValid);
        Assert.IsTrue(readInputCValid);

        Assert.AreEqual(inputA, readInputA);
        Assert.AreEqual(inputB, readInputB);
        Assert.AreEqual(inputC, readInputC);
    }

    [Test]
    public void SimpleOrderTest()
    {
        History<TestData> history = new (3);

        history.Write(0, inputA);
        history.Write(2, inputB);
        history.Write(1, inputC);

        var readInputAValid = history.Read(0, out var readInputA);
        var readInputCValid = history.Read(1, out var readInputC);
        var readInputBValid = history.Read(2, out var readInputB);

        Assert.IsTrue(readInputAValid);
        Assert.IsTrue(readInputBValid);
        Assert.IsTrue(readInputCValid);

        Assert.AreEqual(inputA, readInputA);
        Assert.AreEqual(inputB, readInputB);
        Assert.AreEqual(inputC, readInputC);
    }

    [Test]
    public void TestMaxSize()
    {
        History<TestData> history = new (3);
        
        for (uint i = 0; i < 100; ++i)
        {
            history.Write(i, default);
        }

        Assert.Less(history.Count, 100, "It's less than 100");
        Assert.Less(history.Count, 20, "It's less than 20");
    }

    [Test]
    public void TestValueAfterTrim()
    {
        History<TestData> history = new (3);
        
        for (uint i = 0; i < 100; ++i)
        {
            history.Write(i, default);
        }

        history.Write(101, inputA);
        history.Read(101, out var data);

        Assert.AreEqual(data, inputA);
        Assert.False(history.Find(0, out _));
    }

    [Test]
    public void TestClearPast()
    {
        History<TestData> history = new ();
        
        for (uint i = 0; i < 100; ++i)
        {
            history.Write(i, default);
        }

        history.Write(100, inputA);
        history.ClearPast(100);

        Assert.AreEqual(history.Count, 1);
        Assert.AreEqual(history[0], inputA);
    }

    [Test]
    public void TestClearPastOfNonExistentTick()
    {
        History<TestData> history = new ();
        
        for (uint i = 0; i < 100; ++i)
        {
            history.Write(i, default);
        }

        history.Write(102, inputA);
        history.ClearPast(100);

        Assert.AreEqual(history.Count, 1);
        Assert.AreEqual(history[0], inputA);
    }
}
