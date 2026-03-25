using AdminDashboard.Data;
using AdminDashboard.Entities;
using AdminDashboard.Hubs;
using AdminDashboard.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/customer/product-chat")]
public class CustomerProductChatController : ControllerBase
{
    private readonly AppDbContext _db;

    private readonly IHubContext<NotifyHub> _hub;
    public CustomerProductChatController(AppDbContext db, IHubContext<NotifyHub> hub) { _db = db; _hub = hub; }

    [HttpPost]
    [CustomerAuth]
    public async Task<IActionResult> GetOrCreateThread([FromBody] CreateProductChatRequest req)
    {
        var customerId = GetCustomerId();

        var product = await _db.Products.FindAsync(req.ProductId);
        if (product == null) return NotFound(new { error = "المنتج غير موجود" });
        if (product.AgentId == null) return BadRequest(new { error = "هذا المنتج لا يملك مندوباً" });

        var agentId = product.AgentId.Value;

        var thread = await _db.ProductAgentChats
            .FirstOrDefaultAsync(t => t.CustomerId == customerId &&
                                       t.AgentId == agentId &&
                                       t.ProductId == req.ProductId);

        if (thread == null)
        {
            thread = new ProductAgentChat
            {
                CustomerId = customerId,
                AgentId = agentId,
                ProductId = req.ProductId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.ProductAgentChats.Add(thread);
            await _db.SaveChangesAsync();
        }

        return Ok(new { threadId = thread.Id, agentId });
    }

    [HttpGet("{threadId:int}")]
    [CustomerAuth]
    public async Task<IActionResult> GetThread(int threadId)
    {
        var customerId = GetCustomerId();
        var thread = await _db.ProductAgentChats
            .Include(t => t.Agent)
            .Include(t => t.Product)
            .Include(t => t.Messages.OrderBy(m => m.CreatedAtUtc))
            .FirstOrDefaultAsync(t => t.Id == threadId && t.CustomerId == customerId);

        if (thread == null) return NotFound();

        return Ok(new
        {
            id = thread.Id,
            agentId = thread.AgentId,
            agentName = thread.Agent?.Name ?? "بائع",
            productName = thread.Product?.Name,
            messages = thread.Messages.Select(m => new
            {
                id = m.Id,
                fromAgent = m.FromAgent,
                message = m.Message,
                createdAtUtc = m.CreatedAtUtc
            })
        });
    }

    [HttpPost("{threadId:int}/messages")]
    [CustomerAuth]
    public async Task<IActionResult> SendMessage(int threadId, [FromBody] CustomerSendMessageRequest req)
    {
        var customerId = GetCustomerId();
        var thread = await _db.ProductAgentChats
            .FirstOrDefaultAsync(t => t.Id == threadId && t.CustomerId == customerId);

        if (thread == null) return NotFound();

        var msg = new ProductAgentChatMessage
        {
            ThreadId = threadId,
            FromAgent = false,
            Message = req.Message,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ProductAgentChatMessages.Add(msg);

        _db.Notifications.Add(new Notification
        {
            UserType = NotificationUserType.Agent,
            UserId = thread.AgentId,
            Title = "رسالة جديدة من زبون",
            Body = req.Message.Length > 50 ? req.Message[..50] + "..." : req.Message,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var agentPayload = new
        {
            threadId,
            id = msg.Id,
            fromAgent = false,
            message = msg.Message,
            createdAtUtc = msg.CreatedAtUtc
        };
        await _hub.Clients.Group($"agent-{thread.AgentId}").SendAsync("new_chat_message", agentPayload);

        return Ok(new { msg.Id, msg.CreatedAtUtc });
    }

    [HttpGet("/api/customer/product-chats")]
    [CustomerAuth]
    public async Task<IActionResult> GetMyChats()
    {
        var customerId = GetCustomerId();
        var threads = await _db.ProductAgentChats
            .Include(t => t.Agent)
            .Include(t => t.Product)
            .Include(t => t.Messages)
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.Messages.Max(m => (DateTime?)m.CreatedAtUtc) ?? t.CreatedAtUtc)
            .Select(t => new
            {
                t.Id,
                t.AgentId,
                AgentName = t.Agent != null ? t.Agent.Name : "بائع",
                ProductName = t.Product != null ? t.Product.Name : (string?)null,
                LastMessage = t.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => m.Message).FirstOrDefault(),
                LastMessageAt = t.Messages.Max(m => (DateTime?)m.CreatedAtUtc)
            })
            .ToListAsync();

        return Ok(threads);
    }

    private int GetCustomerId()
    {
        var claim = User.FindFirst("customerId")?.Value
            ?? HttpContext.Items["customerId"]?.ToString();
        return int.TryParse(claim, out var id) ? id : 0;
    }
}

public record CreateProductChatRequest(int ProductId);
public record CustomerSendMessageRequest(string Message);

[ApiController]
[Route("api/customer/agent-chat")]
public class CustomerAgentChatController : ControllerBase
{
    private readonly AppDbContext _db;

    private readonly IHubContext<NotifyHub> _hub2;
    public CustomerAgentChatController(AppDbContext db, IHubContext<NotifyHub> hub) { _db = db; _hub2 = hub; }

    private int GetCustomerIdFromBody(int bodyCustomerId)
    {
        
        var claim = User.FindFirst("customerId")?.Value
            ?? HttpContext.Items["customerId"]?.ToString();
        if (int.TryParse(claim, out var id) && id > 0) return id;
        return bodyCustomerId;
    }

    public record AgentChatCreateReq(int CustomerId, int AgentId);
    public record AgentChatMessageReq(int CustomerId, string Message);

    [HttpPost("thread")]
    [CustomerAuth]
    public async Task<IActionResult> GetOrCreateThread([FromBody] AgentChatCreateReq req)
    {
        var customerId = GetCustomerIdFromBody(req.CustomerId);

        // Verify agent exists
        var agent = await _db.Agents.FindAsync(req.AgentId);
        if (agent == null) return BadRequest(new { error = "المندوب غير موجود" });

        var thread = await _db.ProductAgentChats
            .FirstOrDefaultAsync(t => t.CustomerId == customerId && t.AgentId == req.AgentId && t.ProductId == null);

        if (thread == null)
        {
            thread = new ProductAgentChat
            {
                CustomerId = customerId,
                AgentId = req.AgentId,
                ProductId = null,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.ProductAgentChats.Add(thread);
            await _db.SaveChangesAsync();
        }

        return Ok(new { threadId = thread.Id, agentId = thread.AgentId });
    }

    [HttpGet("thread/{threadId:int}")]
    [CustomerAuth]
    public async Task<IActionResult> GetThread(int threadId)
    {
        var thread = await _db.ProductAgentChats
            .Include(t => t.Agent)
            .Include(t => t.Product)
            .Include(t => t.Messages.OrderBy(m => m.CreatedAtUtc))
            .FirstOrDefaultAsync(t => t.Id == threadId);

        if (thread == null) return NotFound();

        return Ok(new
        {
            id = thread.Id,
            agentId = thread.AgentId,
            agentName = thread.Agent?.Name ?? "بائع",
            productName = thread.Product?.Name,
            messages = thread.Messages.Select(m => new
            {
                id = m.Id,
                fromAgent = m.FromAgent,
                message = m.Message,
                createdAtUtc = m.CreatedAtUtc
            })
        });
    }

    [HttpPost("thread/{threadId:int}/messages")]
    [CustomerAuth]
    public async Task<IActionResult> SendMessage(int threadId, [FromBody] AgentChatMessageReq req)
    {
        var thread = await _db.ProductAgentChats.FindAsync(threadId);
        if (thread == null) return NotFound();

        var msg = new ProductAgentChatMessage
        {
            ThreadId = threadId,
            FromAgent = false,
            Message = req.Message,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ProductAgentChatMessages.Add(msg);

        _db.Notifications.Add(new Notification
        {
            UserType = NotificationUserType.Agent,
            UserId = thread.AgentId,
            Title = "رسالة جديدة من زبون",
            Body = req.Message.Length > 50 ? req.Message[..50] + "..." : req.Message,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Broadcast to agent via SignalR
        var payload = new { threadId, id = msg.Id, fromAgent = false, message = msg.Message, createdAtUtc = msg.CreatedAtUtc };
        await _hub2.Clients.Group($"agent-{thread.AgentId}").SendAsync("new_chat_message", payload);

        return Ok(new { msg.Id, msg.CreatedAtUtc });
    }
}
