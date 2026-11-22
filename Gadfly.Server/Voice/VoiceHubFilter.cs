using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using MindAttic.Avatar.AspNetCore;

namespace Gadfly.Server.Voice;

/// <summary>
/// Guards the library hubs (GAD-T53). Both are also mapped with the Student policy, so only a signed-in student reaches them;
/// this filter adds the exam checks the libraries can't know about: the student must have been armed by their own exam room
/// (<see cref="VoiceGrants"/>), the avatar hub speaks only texts the server offered for that student, and the recognizer opens
/// only while a session is armed.
/// </summary>
public sealed class VoiceHubFilter(VoiceGrants grants, Diagnostics.ApiCallRecorder timings, QuestionVoiceStreams questions, ExaminerTts tts) : IHubFilter
{
    private static Guid Student(HubCallerContext context)
    {
        var tenant = TenantSnapshot.FromPrincipal(context.User, context.GetHttpContext()?.Request.Cookies[ITenantContext.ActiveOrgCookie]);
        return tenant is { UserId: { } user, Role: OrgRoles.Student } ? user : throw new HubException("Sign in as a student to use voice.");
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (!grants.IsArmed(Student(context.Context))) throw new HubException("Voice is only available inside your own exam.");
        await next(context);
    }

    public ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var user = Student(invocation.Context);
        if (!grants.IsArmed(user)) throw new HubException("Voice is only available inside your own exam.");
        // A question spoken as it is written (GAD-T495): Speak with a stream's token plays that stream (the server's own guarded parts,
        // for this student only) on this connection instead of synthesizing the token's text. Any other Speak or a Stop ends it.
        if (invocation.Hub is AvatarSpeechHub)
        {
            var connection = invocation.Context.ConnectionId;
            if (invocation.HubMethodName == nameof(AvatarSpeechHub.Speak) && QuestionVoiceStreams.IsToken(invocation.HubMethodArguments.FirstOrDefault() as string))
                return ValueTask.FromResult<object?>(questions.Play(connection, user, (string)invocation.HubMethodArguments[0]!));
            if (invocation.HubMethodName == nameof(AvatarSpeechHub.Speak)) _ = questions.StopAsync(connection, tell: false);
            else if (invocation.HubMethodName == nameof(AvatarSpeechHub.Stop)) return StopBothAsync(invocation, next, connection);
        }
        if (invocation.Hub is AvatarSpeechHub && invocation.HubMethodName == nameof(AvatarSpeechHub.Speak)
            && !grants.MaySpeak(user, invocation.HubMethodArguments.FirstOrDefault() as string))
            throw new HubException("The examiner can only say what the exam asked.");
        // The examiner's voice is the server's choice (GAD-T475): whatever voice id the browser sent is replaced with the one
        // the exam room chose for this session (null = the platform voice). The arguments are the array the hub method is
        // invoked with; if that ever isn't so, refuse rather than let a browser-chosen id through.
        if (invocation.Hub is AvatarSpeechHub && invocation.HubMethodName == nameof(AvatarSpeechHub.Speak))
        {
            if (invocation.HubMethodArguments is not object?[] { Length: >= 2 } speakArgs)
                throw new HubException("The examiner's voice could not be set.");
            speakArgs[1] = grants.VoiceFor(user, speakArgs[0] as string);
            // A session the library's one voice doesn't fit (another model for the exam language, GAD-T530, or the slower, plainer pace
            // for a second-language speaker, GAD-T555) is spoken by Gadfly's own client instead, on the library's own events. Every
            // other session, every English exam by an English speaker included, goes to the library exactly as before.
            if (grants.OffererOf(user, speakArgs[0] as string) is { Speech: { } speech } offerer && tts.For(speech) is { } profile
                && questions.SampleRate is not null)
            {
                var connection = invocation.Context.ConnectionId;
                var cache = speakArgs.Length >= 3 && speakArgs[2] is true;
                return ValueTask.FromResult<object?>(questions.SpeakText(connection, user, offerer.Organization, offerer.Session,
                    (string)speakArgs[0]!, (string?)speakArgs[1], cache, profile));
            }
        }
        // Vocabulary hints and the spoken language are computed server-side from the exam itself (GAD-T357, GAD-T358) and
        // never trusted from the browser: a StartSession call may ask for the language the exam specifies and any subset
        // of its own vocabulary, nothing else.
        if (invocation.Hub is MindAttic.SpeechRecognition.AspNetCore.SpeechRecognitionHub
            && invocation.HubMethodName == nameof(MindAttic.SpeechRecognition.AspNetCore.SpeechRecognitionHub.StartSession)
            && invocation.HubMethodArguments.FirstOrDefault() is MindAttic.SpeechRecognition.AspNetCore.StartSessionRequest request)
        {
            var allowedLanguage = grants.AllowedLanguage(user);
            if (allowedLanguage is not null && !string.Equals(request.Language, allowedLanguage, StringComparison.OrdinalIgnoreCase))
                throw new HubException("This exam's spoken language is set by the exam, not the browser.");
            if (!grants.IsVocabularyAllowed(user, request.Keyterms))
                throw new HubException("Vocabulary hints must come from the exam itself.");
        }
        return Timed(user, invocation, next);
    }

    /// <summary>Barge-in: stops a streamed question on this connection (telling the browser) and whatever the library is saying.</summary>
    private async ValueTask<object?> StopBothAsync(HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next, string connection)
    {
        await questions.StopAsync(connection, tell: true);
        return await next(invocation);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        if (context.Hub is AvatarSpeechHub) questions.Forget(context.Context.ConnectionId);
        return next(context, exception);
    }

    /// <summary>
    /// Tags the call with the student's session, so the examiner's voice (synthesized in the background from Speak) is timed
    /// against it, and times the recognizer calls a student waits on (GAD-T413). Async so the tag is scoped to this call.
    /// </summary>
    private async ValueTask<object?> Timed(Guid user, HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var current = grants.Current(user);
        using var scope = Diagnostics.ApiCallScope.Begin(current?.Organization, current?.Session);
        var name = invocation.Hub is MindAttic.SpeechRecognition.AspNetCore.SpeechRecognitionHub ? invocation.HubMethodName switch
        {
            "StartSession" => "stt.startSession",
            "Commit" => "stt.commit",
            "StopSession" => "stt.stopSession",
            _ => null,
        } : null;
        if (name is null) return await next(invocation);
        object? result = null;
        await timings.TimeAsync(name, async () => result = await next(invocation));
        return result;
    }
}
