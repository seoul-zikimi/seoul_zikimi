using NUnit.Framework;

namespace GridSystem.Tests
{
    /// <summary>
    /// G-1.2: EditMode 테스트 하네스가 동작하는지 확인하는 스모크 테스트.
    /// (GridSystem 런타임 타입 참조는 G0.1부터 — asmdef 참조 체인 검증.)
    /// </summary>
    public class SmokeTest
    {
        [Test]
        public void Harness_Works()
        {
            Assert.AreEqual(4, 2 + 2);
        }
    }
}
