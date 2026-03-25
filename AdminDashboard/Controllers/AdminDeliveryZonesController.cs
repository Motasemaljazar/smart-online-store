using AdminDashboard.Data;
using AdminDashboard.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/admin/delivery-zones")]
[Authorize(Policy = "AdminOnly")]
public class AdminDeliveryZonesController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminDeliveryZonesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var zones = await _db.DeliveryZones
            .AsNoTracking()
            .OrderBy(z => z.SortOrder)
            .ThenBy(z => z.Id)
            .ToListAsync();

        return Ok(zones);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var zone = await _db.DeliveryZones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == id);
        if (zone == null) return NotFound(new { error = "not_found" });
        return Ok(zone);
    }

    public record UpsertZoneReq(
        int? Id,
        string Name,
        decimal Fee,
        decimal? MinOrder,
        bool IsActive,
        int SortOrder,
        string? PolygonJson
    );

    [HttpPost]
    public async Task<IActionResult> Upsert(UpsertZoneReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "name_required", message = "اسم المنطقة مطلوب" });

        DeliveryZone zone;

        if (req.Id is null)
        {
            zone = new DeliveryZone();
            _db.DeliveryZones.Add(zone);
        }
        else
        {
            zone = await _db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == req.Id.Value)
                   ?? new DeliveryZone();
            if (zone.Id == 0) return NotFound(new { error = "not_found" });
        }

        zone.Name        = req.Name.Trim();
        zone.Fee         = Math.Max(0, req.Fee);
        zone.MinOrder    = req.MinOrder.HasValue ? Math.Max(0, req.MinOrder.Value) : null;
        zone.IsActive    = req.IsActive;
        zone.SortOrder   = req.SortOrder;
        zone.PolygonJson = string.IsNullOrWhiteSpace(req.PolygonJson) ? null : req.PolygonJson.Trim();

        await _db.SaveChangesAsync();

        return Ok(new
        {
            zone.Id,
            zone.Name,
            zone.Fee,
            zone.MinOrder,
            zone.IsActive,
            zone.SortOrder,
            zone.PolygonJson
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var zone = await _db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == id);
        if (zone == null) return NotFound(new { error = "not_found" });

        _db.DeliveryZones.Remove(zone);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
