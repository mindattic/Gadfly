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
public sealed class VoiceHubFilter(VoiceGrants grants, Diagnostics.ApiCallRecorder timings) : IHubFilter
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
        if (invocation.Hub is AvatarSpeechHub && invocation.HubMethodName == nameof(AvatarSpeechHub.Speak)
            && !grants.MaySpeak(user, invocation.HubMethodArguments.FirstOrDefault() as string))
            throw new HubException("The examiner can only say what the exam asked.");
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
