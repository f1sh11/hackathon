namespace SydneyBuddy.Api;

/// <summary>Server-side opt-in: a map or client setting cannot enable paid provider calls.</summary>
public sealed class PaidServicePolicy(IConfiguration configuration)
{
    public bool Allowed => configuration.GetValue<bool>("AllowPaidServices");
    public void RequireAllowed()
    {
        if (!Allowed)
            throw new ServiceException("后端已禁止付费服务。请使用 demo 模式；需要在线测试时由开发者设置 AllowPaidServices=true 并重启后端。");
    }
}
