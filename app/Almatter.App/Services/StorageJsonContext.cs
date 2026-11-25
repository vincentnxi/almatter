using System.Text.Json.Serialization;
using Almatter.App.Models;

namespace Almatter.App.Services;

/// <summary>
/// Compile-time JSON code for the files the app keeps on disk (settings and
/// the remembered session), so they survive trimming; see CoreJsonContext.
/// The output is the same JSON the reflection-based serializer wrote, so
/// existing files load unchanged.
///
/// Named floating-point literals are allowed because AppSettings.WindowX/Y
/// are NaN until the window has been placed once: without this, saving
/// settings before that (the login screen's language switch on a first run)
/// threw and the choice was silently lost.
/// </summary>
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Session))]
internal sealed partial class StorageJsonContext : JsonSerializerContext
{
}
