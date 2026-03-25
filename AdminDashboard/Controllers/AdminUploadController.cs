using AdminDashboard.Data;
using AdminDashboard.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/admin/upload")]
[Authorize(Policy = "AdminOnly")]
public class AdminUploadController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AdminUploadController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    private string ResolveWebRoot()
    {

        var webRoot = _env.WebRootPath;
        if (!string.IsNullOrWhiteSpace(webRoot))
        {
            Directory.CreateDirectory(webRoot);
            return webRoot;
        }

        var fallback = Path.Combine(_env.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    [HttpPost("asset")]
    [RequestSizeLimit(104857600)]
    [RequestFormLimits(MultipartBodyLengthLimit = 104857600)]
    public async Task<IActionResult> UploadAsset([FromQuery] string kind, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "empty_file" });

        // «· Õﬁﬁ „‰ ‰Ê⁄ «·„·› - ’Ê— ›ﬁÿ
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".jfif", ".avif", ".heic", ".heif" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest(new { error = "invalid_file_type", message = "Ìı”„Õ »—›⁄ «·’Ê— ›ﬁÿ (jpg, png, webp, gif...)" });

        kind = (kind ?? "asset").Trim().ToLowerInvariant();

        var folderRel = kind == "offers"
            ? "uploads/offers"
            : $"uploads/assets/{kind}";
        var folderAbs = Path.Combine(ResolveWebRoot(), folderRel);
        Directory.CreateDirectory(folderAbs);

        var name = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
        var abs = Path.Combine(folderAbs, name);

        await using (var fs = System.IO.File.Create(abs))
        {
            await file.CopyToAsync(fs);
        }

        var url = "/" + folderRel + "/" + name;
        return Ok(new { url });
    }

    [HttpPost("product-images/{productId:int}")]
    public async Task<IActionResult> UploadProductImages(int productId, [FromForm] List<IFormFile>? files)
    {

        var product = await _db.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            return NotFound(new { error = "product_not_found" });

        var allFiles = new List<IFormFile>();

        if (Request.Form.Files != null && Request.Form.Files.Count > 0)
            allFiles.AddRange(Request.Form.Files);

        if (files != null && files.Count > 0)
            allFiles.AddRange(files);

        allFiles = allFiles
            .Where(f => f != null && f.Length > 0)
            .Distinct()
            .ToList();

        if (allFiles.Count == 0)
            return BadRequest(new { error = "no_files" });

        var folderRel = $"uploads/products/{productId}";
        var folderAbs = Path.Combine(ResolveWebRoot(), folderRel);
        Directory.CreateDirectory(folderAbs);

        int nextSort =
            product.Images.Count == 0
                ? 0
                : product.Images.Max(i => i.SortOrder) + 1;

        bool setPrimaryNext = !product.Images.Any(i => i.IsPrimary);

        var createdEntities = new List<ProductImage>();

        foreach (var file in allFiles)
        {
            var ext = Path.GetExtension(file.FileName);
            var name = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
            var abs = Path.Combine(folderAbs, name);

            await using (var fs = System.IO.File.Create(abs))
            {
                await file.CopyToAsync(fs);
            }

            var url = "/" + folderRel + "/" + name;

            var img = new ProductImage
            {
                ProductId = productId,
                Url = url,
                SortOrder = nextSort++,
                IsPrimary = setPrimaryNext
            };

            if (setPrimaryNext) setPrimaryNext = false;

            _db.ProductImages.Add(img);
            createdEntities.Add(img);
        }

        await _db.SaveChangesAsync();

        var created = createdEntities
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.Url, i.SortOrder, i.IsPrimary })
            .ToList();

        return Ok(new { images = created });
    }

    [HttpDelete("product-images/{productId:int}")]
    public async Task<IActionResult> ClearProductImages(int productId)
    {
        var imgs = await _db.ProductImages.Where(i => i.ProductId == productId).ToListAsync();
        _db.ProductImages.RemoveRange(imgs);
        await _db.SaveChangesAsync();

        try
        {
            var folderAbs = Path.Combine(ResolveWebRoot(), "uploads", "products", productId.ToString());
            if (Directory.Exists(folderAbs))
                Directory.Delete(folderAbs, recursive: true);
        }
        catch
        {

        }

        return Ok(new { ok = true });
    }

    [HttpDelete("product-image/{imageId:int}")]
    public async Task<IActionResult> DeleteProductImage(int imageId)
    {
        var img = await _db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId);
        if (img == null) return NotFound(new { error = "not_found" });

        try
        {
            if (!string.IsNullOrWhiteSpace(img.Url) && img.Url.StartsWith("/"))
            {
                var rel = img.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var abs = Path.Combine(ResolveWebRoot(), rel);
                if (System.IO.File.Exists(abs))
                    System.IO.File.Delete(abs);
            }
        }
        catch
        {

        }

        _db.ProductImages.Remove(img);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("product-image/{imageId:int}/primary")]
    public async Task<IActionResult> SetPrimary(int imageId)
    {
        var img = await _db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId);
        if (img == null) return NotFound(new { error = "not_found" });

        var all = await _db.ProductImages.Where(i => i.ProductId == img.ProductId).ToListAsync();
        foreach (var i in all) i.IsPrimary = i.Id == img.Id;

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("product-image/{imageId:int}/move")]
    public async Task<IActionResult> MoveImage(int imageId, [FromQuery] string dir)
    {
        dir = (dir ?? "").Trim().ToLowerInvariant();
        if (dir != "up" && dir != "down") return BadRequest(new { error = "invalid_dir" });

        var img = await _db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId);
        if (img == null) return NotFound(new { error = "not_found" });

        var imgs = await _db.ProductImages
            .Where(i => i.ProductId == img.ProductId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToListAsync();

        var idx = imgs.FindIndex(i => i.Id == imageId);
        if (idx < 0) return NotFound(new { error = "not_found" });

        var swapWith = dir == "up" ? idx - 1 : idx + 1;
        if (swapWith < 0 || swapWith >= imgs.Count) return Ok(new { ok = true });

        var a = imgs[idx];
        var b = imgs[swapWith];

        (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
