using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HospitalApi.Hubs;

/// <summary>Server-to-client push only. Each user receives events addressed to their own id.</summary>
[Authorize]
public class NotificationsHub : Hub;
