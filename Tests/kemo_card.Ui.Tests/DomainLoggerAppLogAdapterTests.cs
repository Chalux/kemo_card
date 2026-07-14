using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class DomainLoggerAppLogAdapterTests
{
	[Test]
	public void EventDispatcherLogger_forwards_error_with_Mvc_category()
	{
		var recording = new RecordingAppLog();
		var logger = new GodotEventDispatcherLogger(recording);

		logger.LogError("boom");

		Assert.That(recording.Entries, Has.Count.EqualTo(1));
		Assert.That(recording.Entries[0], Is.EqualTo(new AppLogEntry(AppLogLevel.Error, "boom", "Mvc")));
	}

	[Test]
	public void ContentModLogger_uses_ContentMod_category_without_duplicate_prefix()
	{
		var recording = new RecordingAppLog();
		var logger = new GodotContentModLogger(recording);

		logger.LogSkipped(new ModSkipEntry("mod.a", ModSkipReason.InvalidManifest, "bad"));
		logger.LogConflict(new ContentIdConflictEntry(EContentCategory.Card, "strike", "winner", "loser"));
		logger.LogValidationError(new ContentDefinitionValidationError(EContentCategory.Card, "strike", "missing"));
		logger.LogScriptLoadError(new ScriptLoadError("mod.a", "main.js", "syntax"));

		Assert.That(recording.Entries, Has.Count.EqualTo(4));
		Assert.That(recording.Entries, Has.All.Property("Category").EqualTo("ContentMod"));
		Assert.That(recording.Entries, Has.All.Property("Level").EqualTo(AppLogLevel.Warning));
		Assert.That(recording.Entries[0].Message, Does.StartWith("Skipped "));
		Assert.That(recording.Entries[0].Message, Does.Not.Contain("[ContentMod]"));
		Assert.That(recording.Entries[1].Message, Does.Contain("Id conflict"));
		Assert.That(recording.Entries[2].Message, Does.Contain("Validation"));
		Assert.That(recording.Entries[3].Message, Does.Contain("Script load"));
	}

	[Test]
	public void ModScriptLogger_forwards_as_Info_with_Script_category()
	{
		var recording = new RecordingAppLog();
		var logger = new AppLogModScriptLogger(recording);

		logger.Log("hello");

		Assert.That(recording.Entries, Has.Count.EqualTo(1));
		Assert.That(recording.Entries[0], Is.EqualTo(new AppLogEntry(AppLogLevel.Info, "hello", "Script")));
	}
}
