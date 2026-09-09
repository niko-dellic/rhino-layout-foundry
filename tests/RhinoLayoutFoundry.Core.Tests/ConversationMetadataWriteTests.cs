using RhinoLayoutFoundry.Extensibility;
using Xunit;

namespace RhinoLayoutFoundry.Core.Tests;

public class ConversationMetadataWriteTests
{
    [Fact]
    public void ScopeIsDocumentBoundAndRestoresAfterNestedFailure()
    {
        Assert.False(ConversationMetadataWrite.IsActive(1));
        ConversationMetadataWrite.Run(1, () =>
        {
            Assert.True(ConversationMetadataWrite.IsActive(1));
            Assert.False(ConversationMetadataWrite.IsActive(2));
            Assert.Throws<InvalidOperationException>(() => ConversationMetadataWrite.Run(2, () =>
            {
                Assert.False(ConversationMetadataWrite.IsActive(1));
                Assert.True(ConversationMetadataWrite.IsActive(2));
                throw new InvalidOperationException();
            }));
            Assert.True(ConversationMetadataWrite.IsActive(1));
        });
        Assert.False(ConversationMetadataWrite.IsActive(1));
    }

    [Fact]
    public void ScopeDoesNotLeakToAnotherThread()
    {
        ConversationMetadataWrite.Run(1, () =>
        {
            var active = true;
            var thread = new Thread(() => active = ConversationMetadataWrite.IsActive(1));
            thread.Start();
            thread.Join();
            Assert.False(active);
        });
    }
}
