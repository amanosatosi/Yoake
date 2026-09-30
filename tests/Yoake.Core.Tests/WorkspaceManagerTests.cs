using System.ComponentModel;
using Yoake.Core.Workspace;

namespace Yoake.Core.Tests;

public sealed class WorkspaceManagerTests
{
    [Fact]
    public void ClosingActiveDocumentSelectsNeighbor()
    {
        var workspace = new WorkspaceManager();
        var first = workspace.CreateUntitled();
        var second = workspace.CreateUntitled();
        Assert.Equal(second.Id, workspace.ActiveDocumentId);
        Assert.True(workspace.Close(second.Id));
        Assert.Equal(first.Id, workspace.ActiveDocumentId);
    }

    [Fact]
    public void DocumentSessionNotifiesTitleAndDirtyChanges()
    {
        var document = new DocumentSession(Guid.NewGuid(), "Before");
        var properties = new List<string?>();
        document.PropertyChanged += (_, args) => properties.Add(args.PropertyName);

        document.Title = "After";
        document.IsDirty = true;

        Assert.Contains(nameof(DocumentSession.Title), properties);
        Assert.Contains(nameof(DocumentSession.IsDirty), properties);
    }
}
