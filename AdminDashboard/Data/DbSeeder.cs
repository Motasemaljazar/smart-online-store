using AdminDashboard.Entities;
using AdminDashboard.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace AdminDashboard.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, bool isDevelopment, IConfiguration? config = null)
    {
        if (!await db.AdminUsers.AnyAsync())
        {
            if (isDevelopment)
            {
                var (hash, salt) = AdminPassword.HashPassword("admin123");
                db.AdminUsers.Add(new AdminUser
                {
                    Email        = "admin",
                    PasswordHash = hash,
                    PasswordSalt = salt
                });
            }
            else
            {
                var email = config?["InitialAdmin:Email"];
                var pass  = config?["InitialAdmin:Password"];
                if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(pass))
                {
                    var (hash, salt) = AdminPassword.HashPassword(pass);
                    db.AdminUsers.Add(new AdminUser
                    {
                        Email        = email.Trim(),
                        PasswordHash = hash,
                        PasswordSalt = salt
                    });
                }
                else
                {
                    throw new InvalidOperationException(
                        "No admin user found. In Production, set InitialAdmin:Email and InitialAdmin:Password.");
                }
            }
        }

        if (!await db.StoreSettings.AnyAsync())
        {
            db.StoreSettings.Add(new StoreSettings
            {
                StoreName         = "سوق نت",
                PrimaryColorHex   = "#FF9900",
                SecondaryColorHex = "#131921",
                OffersColorHex    = "#CC0C39",
                WelcomeText       = "مرحباً بك في سوق نت — تسوّق بذكاء",
                WorkHours         = "",
                StoreLat          = 0,
                StoreLng          = 0,
                IsManuallyClosed  = false,
                ClosedMessage     = "المتجر مغلق حالياً، نعود قريباً",
                MinOrderAmount    = 0,
                DeliveryFeeType   = DeliveryFeeType.Fixed,
                DeliveryFeeValue  = 0,
                SupportPhone      = "",
                SupportWhatsApp   = "",
                IsAcceptingOrders = false,
                RoutingProfile    = "driving"
            });
        }

        await db.SaveChangesAsync();

        if (!await db.Agents.AnyAsync())
            await SeedAgentsAsync(db);

        if (!await db.Categories.AnyAsync())
            await SeedCategoriesAndProductsAsync(db);

        var settings = await db.StoreSettings.FirstOrDefaultAsync();
        if (settings != null && string.IsNullOrWhiteSpace(settings.LogoUrl))
            await SeedLogoAndBannersAsync(db, settings);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  8 AGENTS  —  pin = 1234  —  أرقام هواتف: 0 / 00 / 000 / ...
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedAgentsAsync(AppDbContext db)
    {
        const string pin = "1234";

        var agentData = new[]
        {
            new { Name = "مندوب الهواتف",          Phone = "0",         Email = "phones@souqnet.com"      },
            new { Name = "مندوب اللابتوبات",        Phone = "00",        Email = "laptops@souqnet.com"     },
            new { Name = "مندوب الأجهزة المنزلية", Phone = "000",       Email = "appliances@souqnet.com"  },
            new { Name = "مندوب المفروشات",         Phone = "0000",      Email = "furniture@souqnet.com"   },
            new { Name = "مندوب الملابس",           Phone = "00000",     Email = "fashion@souqnet.com"     },
            new { Name = "مندوب الألعاب",           Phone = "000000",    Email = "toys@souqnet.com"        },
            new { Name = "مندوب الكتب",             Phone = "0000000",   Email = "books@souqnet.com"       },
            new { Name = "مندوب الرياضة",           Phone = "00000000",  Email = "sports@souqnet.com"      },
        };

        foreach (var ad in agentData)
        {
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(pin, workFactor: 12);
            db.Agents.Add(new Agent
            {
                Name              = ad.Name,
                Phone             = ad.Phone,
                Email             = ad.Email,
                Pin               = pin,
                PasswordHash      = passwordHash,
                PhotoUrl          = $"https://api.dicebear.com/7.x/initials/svg?seed={Uri.EscapeDataString(ad.Name)}&backgroundColor=FF9900",
                Status            = AgentStatus.Offline,
                CommissionPercent = 10m,
                CreatedAtUtc      = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LOGO  +  BANNERS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedLogoAndBannersAsync(AppDbContext db, StoreSettings s)
    {
        s.LogoUrl              = "https://images.unsplash.com/photo-1523474253046-8cd2748b5fd2?w=300&h=300&fit=crop&q=80";
        s.SplashBackground1Url = "https://images.unsplash.com/photo-1556742049-0cfed4f6a45d?w=800&q=80";
        s.SplashBackground2Url = "https://images.unsplash.com/photo-1472851294608-062f824d29cc?w=800&q=80";

        var banners = new[]
        {
            new {
                id = 1,
                imageUrl   = "https://images.unsplash.com/photo-1511707171634-5f897ff02aa9?w=900&h=380&fit=crop&q=80",
                title      = "أحدث الهواتف الذكية 📱",
                subtitle   = "اكتشف أحدث الإصدارات بأفضل الأسعار المضمونة",
                actionType = "category", actionValue = "1"
            },
            new {
                id = 2,
                imageUrl   = "https://images.unsplash.com/photo-1496181133206-80ce9b88a853?w=900&h=380&fit=crop&q=80",
                title      = "لابتوبات للعمل والترفيه 💻",
                subtitle   = "أداء فائق وتصاميم رفيعة لكل احتياجاتك",
                actionType = "category", actionValue = "2"
            },
            new {
                id = 3,
                imageUrl   = "https://images.unsplash.com/photo-1586023492125-27b2c045efd7?w=900&h=380&fit=crop&q=80",
                title      = "أثاث منزلي فاخر 🛋️",
                subtitle   = "اجعل بيتك مكاناً يعكس ذوقك الرفيع",
                actionType = "category", actionValue = "4"
            },
            new {
                id = 4,
                imageUrl   = "https://images.unsplash.com/photo-1441986300917-64674bd600d8?w=900&h=380&fit=crop&q=80",
                title      = "أزياء عصرية لكل المواسم 👗",
                subtitle   = "تشكيلات حصرية من أشهر الماركات العالمية",
                actionType = "category", actionValue = "5"
            },
            new {
                id = 5,
                imageUrl   = "https://images.unsplash.com/photo-1607082348824-0a96f2a4b9da?w=900&h=380&fit=crop&q=80",
                title      = "عروض اليوم الحصرية 🔥",
                subtitle   = "خصومات تصل إلى 70% على آلاف المنتجات",
                actionType = "offers", actionValue = ""
            },
        };
        s.HomeBannersJson = JsonSerializer.Serialize(banners);

        var onboarding = new[]
        {
            new {
                id = 1,
                imageUrl = "https://images.unsplash.com/photo-1607082348824-0a96f2a4b9da?w=600&h=700&fit=crop&q=80",
                title    = "مرحباً بك في سوق نت",
                subtitle = "ملايين المنتجات بأفضل الأسعار توصل إلى باب بيتك"
            },
            new {
                id = 2,
                imageUrl = "https://images.unsplash.com/photo-1526367790999-0150786686a2?w=600&h=700&fit=crop&q=80",
                title    = "توصيل سريع وآمن",
                subtitle = "نضمن لك وصول طلبك بأمان وفي الوقت المحدد"
            },
            new {
                id = 3,
                imageUrl = "https://images.unsplash.com/photo-1556742049-0cfed4f6a45d?w=600&h=700&fit=crop&q=80",
                title    = "دفع آمن ومضمون",
                subtitle = "طرق دفع متعددة مع ضمان استرجاع كامل"
            },
        };
        s.OnboardingJson = JsonSerializer.Serialize(onboarding);

        await db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  8 CATEGORIES  +  40 PRODUCTS
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task SeedCategoriesAndProductsAsync(AppDbContext db)
    {
        var categories = new[]
        {
            new Category { Name = "الهواتف الذكية",      SortOrder = 1, IsActive = true,
                ImageUrl = "https://images.unsplash.com/photo-1511707171634-5f897ff02aa9?w=400&q=80" },
            new Category { Name = "اللابتوبات والحاسبات",SortOrder = 2, IsActive = true,
                ImageUrl = "https://images.unsplash.com/photo-1496181133206-80ce9b88a853?w=400&q=80" },
            new Category { Name = "الأجهزة المنزلية",    SortOrder = 3, IsActive = true,
                ImageUrl = "https://images.unsplash.com/photo-1556909114-f6e7ad7d3136?w=400&q=80" },
            new Category { Name = "المفروشات والأثاث",   SortOrder = 4, IsActive = true,
                ImageUrl = "https://images.unsplash.com/photo-1586023492125-27b2c045efd7?w=400&q=80" },
            new Category { Name = "الأزياء والملابس",    SortOrder = 5, IsActive = true,
                ImageUrl = "https://images.unsplash.com/photo-1441986300917-64674bd600d8?w=400&q=80" },
            new Category { Name = "الألعاب والترفيه",    SortOrder = 6, IsActive = true,
                ImageUrl = "https://images.unsplash.com/photo-1558060370-d644479cb6f7?w=400&q=80" },
            new Category { Name = "الكتب والقرطاسية",    SortOrder = 7, IsActive = true,
                ImageUrl = "https://images.unsplash.com/photo-1495446815901-a7297e633e8d?w=400&q=80" },
            new Category { Name = "الرياضة واللياقة",    SortOrder = 8, IsActive = true,
                ImageUrl = "https://images.unsplash.com/photo-1517836357463-d25dfeac3438?w=400&q=80" },
        };

        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();

        var phones     = categories[0];
        var laptops    = categories[1];
        var appliances = categories[2];
        var furniture  = categories[3];
        var fashion    = categories[4];
        var toys       = categories[5];
        var books      = categories[6];
        var sports     = categories[7];

        var agents = await db.Agents.OrderBy(a => a.Id).ToListAsync();
        int? AgentId(int idx) => agents.Count > idx ? agents[idx].Id : (int?)null;

        void AddProduct(Category cat, int agentIdx, string name, string desc,
                        decimal price, string imageUrl)
        {
            db.Products.Add(new Product
            {
                CategoryId    = cat.Id,
                AgentId       = AgentId(agentIdx),
                Name          = name,
                Description   = desc,
                Price         = price,
                IsActive      = true,
                IsAvailable   = true,
                TrackStock    = false,
                StockQuantity = 0,
                ImageUrl      = imageUrl,
            });
        }

        // ── 1. الهواتف الذكية ─────────────────────────────────────────────────
        AddProduct(phones, 0, "سامسونج Galaxy S24 Ultra",
            "هاتف فلاغشيب 2024 بشاشة Dynamic AMOLED 2X مقاس 6.8 بوصة بدقة QHD+، معالج Snapdragon 8 Gen 3، كاميرا رئيسية 200 ميغابيكسل مع تقنية AI المتقدمة، بطارية 5000 مللي أمبير مع شحن سريع 45W، ذاكرة 12GB RAM وتخزين 256GB.",
            4299.00m, "https://images.unsplash.com/photo-1610945415295-d9bbf067e59c?w=600&q=80");

        AddProduct(phones, 0, "آيفون 15 Pro Max",
            "أحدث إصدارات آبل بإطار تيتانيوم متين، شاشة Super Retina XDR 6.7 بوصة ProMotion 120Hz، شريحة A17 Pro الأقوى في تاريخ آبل، منظومة كاميرات ثلاثية 48MP مع تقريب بصري 5x، وقدرة تصوير فيديو ProRes بجودة 4K.",
            4899.00m, "https://images.unsplash.com/photo-1695048133142-1a20484d2569?w=600&q=80");

        AddProduct(phones, 0, "شاومي 14 Pro",
            "هاتف ذكي بكاميرا Leica الاحترافية مقاس 50 ميغابيكسل مع عدسات فاريابل، شاشة LTPO AMOLED 6.73 بوصة بمعدل تحديث 120Hz، معالج Snapdragon 8 Gen 3، شحن لاسلكي توربو 120W يشحن البطارية خلال 23 دقيقة فقط.",
            3199.00m, "https://images.unsplash.com/photo-1598327105666-5b89351aff97?w=600&q=80");

        AddProduct(phones, 0, "سامسونج Galaxy A55",
            "هاتف متوسط الفئة بمواصفات رائدة: شاشة Super AMOLED 6.6 بوصة FHD+ 120Hz، معالج Exynos 1480، كاميرا خلفية ثلاثية 50MP، بطارية 5000 مللي أمبير مع شحن 25W، مقاومة للماء IP67، ذاكرة عشوائية 8GB.",
            1799.00m, "https://images.unsplash.com/photo-1567581935884-3349723552ca?w=600&q=80");

        AddProduct(phones, 0, "جوجل Pixel 8 Pro",
            "هاتف جوجل الرائد بأقوى معالج Tensor G3 المصنوع خصيصاً لتطبيقات الذكاء الاصطناعي، كاميرا 50MP بأفضل برمجيات التصوير في السوق، شاشة LTPO OLED 6.7 بوصة، 7 سنوات من تحديثات الأمان المضمونة.",
            3599.00m, "https://images.unsplash.com/photo-1591337676887-a217a6970a8a?w=600&q=80");

        // ── 2. اللابتوبات ─────────────────────────────────────────────────────
        AddProduct(laptops, 1, "ماك بوك برو M3 Pro",
            "لابتوب احترافي من آبل بشريحة M3 Pro ذات 12 نواة، شاشة Liquid Retina XDR 14.2 بوصة بدقة 3024×1964، ذاكرة موحدة 18GB وتخزين SSD 512GB، بطارية تدوم 18 ساعة، مثالي للمصممين والمطورين والمحررين.",
            7999.00m, "https://images.unsplash.com/photo-1517336714731-489689fd1ca8?w=600&q=80");

        AddProduct(laptops, 1, "ديل XPS 15",
            "لابتوب إبداعي بشاشة OLED 15.6 بوصة بدقة 3.5K مذهلة، معالج Intel Core i7-13700H، كرت شاشة NVIDIA RTX 4060، ذاكرة 16GB DDR5، SSD 512GB NVMe، بناء ألومنيوم فاخر ووزن خفيف 1.86 كيلوجرام.",
            5499.00m, "https://images.unsplash.com/photo-1593642632559-0c6d3fc62b89?w=600&q=80");

        AddProduct(laptops, 1, "لينوفو ThinkPad X1 Carbon",
            "لابتوب الأعمال الأيقوني بوزن 1.12 كيلوجرام فقط، معالج Intel Core i7 الجيل 13، شاشة IPS 14 بوصة مضادة للانعكاس، بطارية تدوم 15 ساعة، لوحة مفاتيح ThinkPad الشهيرة، وشهادة MIL-SPEC للمتانة القصوى.",
            4899.00m, "https://images.unsplash.com/photo-1496181133206-80ce9b88a853?w=600&q=80");

        AddProduct(laptops, 1, "آسوس ROG Strix G16",
            "لابتوب ألعاب بمعالج Intel Core i9-13980HX، كرت شاشة NVIDIA RTX 4080 بـ 12GB GDDR6، شاشة QHD+ 16 بوصة 240Hz بتقنية G-Sync، ذاكرة 32GB DDR5، SSD 1TB NVMe، نظام تبريد مزدوج بـ 5 مراوح.",
            8999.00m, "https://images.unsplash.com/photo-1603302576837-37561b2e2302?w=600&q=80");

        AddProduct(laptops, 1, "HP Spectre x360 14",
            "لابتوب 2-in-1 فاخر قابل للطي 360 درجة، شاشة OLED تعمل باللمس 14 بوصة 2.8K، معالج Intel Core Ultra 7، ذاكرة 16GB LPDDR5، بطارية 66Wh مع شحن سريع، يأتي مع قلم HP Tilt Pen للرسم والكتابة.",
            4299.00m, "https://images.unsplash.com/photo-1544099858-75349571f93e?w=600&q=80");

        // ── 3. الأجهزة المنزلية ───────────────────────────────────────────────
        AddProduct(appliances, 2, "ثلاجة LG InstaView Door-in-Door",
            "ثلاجة بسعة 635 لتر بتقنية InstaView للكشف عن المحتوى بدون فتح الباب، نظام تبريد Linear Cooling، مبرد مياه داخلي، موزع ثلج وماء، تقنية ThinQ للتحكم عبر الهاتف، تصنيف طاقة A+++.",
            5299.00m, "https://images.unsplash.com/photo-1584568694244-14fbdf83bd30?w=600&q=80");

        AddProduct(appliances, 2, "غسالة سامسونج AddWash",
            "غسالة أمامية 9 كيلوجرام بتقنية AddWash لإضافة الملابس أثناء الغسيل، محرك Inverter الصامت، برنامج الغسيل السريع 15 دقيقة، نظام بخار للتعقيم، واي فاي للتحكم عن بعد، تصنيف طاقة A+++.",
            2899.00m, "https://images.unsplash.com/photo-1610557892470-55d9e80c0bce?w=600&q=80");

        AddProduct(appliances, 2, "مكيف سبليت Inverter 24000 BTU",
            "مكيف سبليت ذكي بتقنية Inverter لتوفير 60% من الكهرباء، تبريد وتدفئة وتنقية هواء في جهاز واحد، فلتر PM2.5 لتنقية الهواء من الجراثيم، تحكم صوتي وعبر التطبيق، كفاءة طاقة A+++.",
            3499.00m, "https://images.unsplash.com/photo-1631049307264-da0ec9d70304?w=600&q=80");

        AddProduct(appliances, 2, "فرن بوش متعدد الوظائف",
            "فرن كهربائي مدمج 71 لتر بتقنية HotAir Plus لتوزيع الحرارة المتساوي، 13 برنامجاً للطهي، شاشة TFT ملونة، تنظيف ذاتي بالبخار، ترمومتر اللحم المدمج، مادة EcoClean الداخلية لسهولة التنظيف.",
            3799.00m, "https://images.unsplash.com/photo-1556909114-f6e7ad7d3136?w=600&q=80");

        AddProduct(appliances, 2, "مكنسة دايسون V15 Detect",
            "مكنسة لاسلكية بتقنية الليزر لاكتشاف الغبار الدقيق غير المرئي، محرك Hyperdymium 240,000 دورة/دقيقة، فلتر HEPA يحبس 99.99% من الجراثيم، بطارية 60 دقيقة، شاشة LCD لعرض نوع الغبار وعمر البطارية.",
            2199.00m, "https://images.unsplash.com/photo-1558618666-fcd25c85cd64?w=600&q=80");

        // ── 4. المفروشات ──────────────────────────────────────────────────────
        AddProduct(furniture, 3, "أريكة زاوية L فاخرة",
            "أريكة زاوية عصرية بقماش مخمل إيطالي فاخر مقاوم للبقع، إطار من خشب الزان الأوروبي، وسائد ريش البط الطبيعي للراحة القصوى، أرجل ذهبية من الفولاذ المقاوم للصدأ، متاحة بألوان: رمادي، أزرق، بيج.",
            4599.00m, "https://images.unsplash.com/photo-1555041469-a586c61ea9bc?w=600&q=80");

        AddProduct(furniture, 3, "سرير ملكي مع لوحة خشبية",
            "سرير King Size 180×200 سم بإطار من خشب الجوز المصمت الطبيعي، لوحة رأس منحوتة يدوياً، قواعد داعمة قابلة للتعديل، تشطيب بالورنيش الطبيعي الشفاف، يأتي مع درجين للتخزين، سهل التجميع مع دليل تعليمي.",
            3299.00m, "https://images.unsplash.com/photo-1505693416388-ac5ce068fe85?w=600&q=80");

        AddProduct(furniture, 3, "مكتب دراسة وعمل L-Shape",
            "مكتب L شكل بسطح من الخشب الهندسي المقاوم للخدش والرطوبة، مساحة سطح 160×120 سم، تمديدات كابلات مدمجة، رف علوي لشاشتين، أدراج جانبية مع قفل، مثالي لمحطات العمل الاحترافية والألعاب.",
            1899.00m, "https://images.unsplash.com/photo-1593642632559-0c6d3fc62b89?w=600&q=80");

        AddProduct(furniture, 3, "طاولة طعام رخامية 6 أشخاص",
            "طاولة طعام من الرخام الطبيعي الإيطالي على قاعدة من الفولاذ المقاوم للصدأ، مقاس 180×90 سم، سطح رخام كالاكاتا الأبيض الفاخر بعروق ذهبية، محمية بطبقة سيراميك مقاومة للحرارة والخدش.",
            6999.00m, "https://images.unsplash.com/photo-1586023492125-27b2c045efd7?w=600&q=80");

        AddProduct(furniture, 3, "خزانة ملابس 6 أبواب",
            "خزانة ملابس واسعة بـ 6 أبواب منزلقة بمرايا كاملة، مساحة داخلية مقسمة: 3 أقسام للتعليق، 8 أدراج، رف أحذية سفلي، إضاءة LED تلقائية عند الفتح، تركيب احترافي متضمن مع الشراء.",
            3899.00m, "https://images.unsplash.com/photo-1558997519-83ea9252edf8?w=600&q=80");

        // ── 5. الأزياء ────────────────────────────────────────────────────────
        AddProduct(fashion, 4, "جاكيت جلد رجالي فاخر",
            "جاكيت من الجلد الطبيعي الإيطالي 100%، بطانة من الحرير الطبيعي، غرز يدوية بدقة عالية، جيوب داخلية متعددة، متاح بمقاسات S إلى XXL، بألوان: أسود، بني كونياك، داكن نيفي. يقدم في علبة هدايا أنيقة.",
            1299.00m, "https://images.unsplash.com/photo-1551028719-00167b16eac5?w=600&q=80");

        AddProduct(fashion, 4, "فستان سهرة نسائي",
            "فستان سهرة فاخر من الحرير الطبيعي مع طبقة تول، تطريز يدوي بالخيوط الذهبية والفضية، قصّة A-Line تناسب جميع الأجسام، متاح بألوان: ذهبي شمبانيا، أزرق ملكي، أسود كلاسيك، مقاسات XS-XXL.",
            899.00m, "https://images.unsplash.com/photo-1515372039744-b8f02a3ae446?w=600&q=80");

        AddProduct(fashion, 4, "حذاء رياضي نايكي Air Max 270",
            "حذاء رياضي أيقوني بوحدة Air Max كبيرة للامتصاص المثالي للصدمات، جزء علوي من الشبكة الهندسية المهندسة للتهوية، نعل مطاطي متعدد الطبقات لمقاومة الانزلاق، متاح بأحجام 36-48 وألوان متعددة.",
            649.00m, "https://images.unsplash.com/photo-1542291026-7eec264c27ff?w=600&q=80");

        AddProduct(fashion, 4, "ساعة يد كلاسيكية فاخرة",
            "ساعة يد رجالية بحركة أوتوماتيكية Swiss Made، علبة من الفولاذ المقاوم للصدأ 316L، زجاج ياقوتي مقاوم للخدش، مقاومة للماء حتى 100 متر، سوار جلدي من جلد العجل الإيطالي، ضمان 3 سنوات.",
            2499.00m, "https://images.unsplash.com/photo-1523275335684-37898b6baf30?w=600&q=80");

        AddProduct(fashion, 4, "حقيبة جلدية نسائية",
            "حقيبة يد نسائية من الجلد الطبيعي الإيطالي المدبوغ نباتياً، بطانة قماش مقاومة للبقع، جيوب تنظيمية متعددة، حزام كتف قابل للفصل، متاحة بألوان: كاميل، أسود، تان، بوردو.",
            799.00m, "https://images.unsplash.com/photo-1548036328-c9fa89d128fa?w=600&q=80");

        // ── 6. الألعاب ────────────────────────────────────────────────────────
        AddProduct(toys, 5, "بلايستيشن 5 Slim",
            "كونسول الجيل التاسع من سوني بمعالج AMD Zen 2 ومعالج رسومات RDNA 2، SSD مخصص بسرعة 5.5 جيجابايت/ثانية، دعم 4K/120fps، ذراع DualSense مع ردود فعل لمسية وزنادات تكيفية، تخزين 1TB قابل للتوسعة.",
            2299.00m, "https://images.unsplash.com/photo-1606813907291-d86efa9b94db?w=600&q=80");

        AddProduct(toys, 5, "ليغو تيكنيك السيارة الرياضية",
            "مجموعة ليغو تيكنيك 1458 قطعة لبناء سيارة رياضية بمحرك وظيفي، ناقل حركة 8 سرعات، تعليق أمامي وخلفي، عجلة قيادة وظيفية، مثالية للأعمار 10+ وتنمي مهارات الهندسة والتفكير المنطقي.",
            449.00m, "https://images.unsplash.com/photo-1558060370-d644479cb6f7?w=600&q=80");

        AddProduct(toys, 5, "طائرة درون DJI Mini 4 Pro",
            "طائرة مسيّرة خفيفة الوزن 249 جرام، كاميرا 4K/60fps مع تثبيت جيمبال 3 محاور، تحليق لمسافة 20 كم، بطارية 34 دقيقة، تجنب عوائق من 4 اتجاهات، مثالية للتصوير الجوي الاحترافي.",
            2899.00m, "https://images.unsplash.com/photo-1473968512647-3e447244af8f?w=600&q=80");

        AddProduct(toys, 5, "روبوت LEGO Mindstorms",
            "مجموعة بناء روبوت تعليمية بـ 949 قطعة، قابل للبرمجة بـ Scratch وPython، 5 أنماط روبوت مختلفة، مستشعرات صوت ولون ومسافة، تطبيق مجاني بـ 50 مهمة تعليمية، للأعمار 10-16 سنة.",
            799.00m, "https://images.unsplash.com/photo-1485827404703-89b55fcc595e?w=600&q=80");

        AddProduct(toys, 5, "مجموعة ألعاب خشبية تعليمية",
            "مجموعة 45 قطعة من الخشب الطبيعي المعالج غير السام، تشمل: أشكال هندسية، حروف وأرقام، مكعبات ألوان، ألوان صديقة للبيئة، محفوظة في حقيبة قماشية، تنمي الذكاء المكاني والمهارات الحركية الدقيقة.",
            149.00m, "https://images.unsplash.com/photo-1560851889-ca6e41b9f3d6?w=600&q=80");

        // ── 7. الكتب والقرطاسية ──────────────────────────────────────────────
        AddProduct(books, 6, "مجموعة كتب تطوير الذات الأكثر مبيعاً",
            "مجموعة 5 كتب من أكثر الكتب مبيعاً عالمياً: العادات الذرية، تفكير بلا تفكير، قوة العقل الباطن، فن اللامبالاة، ومبدأ 80/20. جميعها باللغة العربية، ورق عالي الجودة في علبة هدايا أنيقة.",
            299.00m, "https://images.unsplash.com/photo-1512820790803-83ca734da794?w=600&q=80");

        AddProduct(books, 6, "قاموس أكسفورد عربي-إنجليزي الشامل",
            "القاموس الأشمل يضم 120,000 كلمة وتعبير، مع أمثلة استخدام سياقي، جذور لغوية، مصطلحات علمية وتقنية وقانونية، طبعة 2024 محدثة، غلاف مقوى فاخر، 1800 صفحة.",
            185.00m, "https://images.unsplash.com/photo-1495446815901-a7297e633e8d?w=600&q=80");

        AddProduct(books, 6, "طقم أقلام مونت بلان للكتابة",
            "طقم قلم حبر وقلم جاف فاخر، علبة أسطوانية من الخشب المحفور، نصل ذهبي 14 قيراط، حبر أسود فاخر، تغليف هدايا مميز. مثالي لرجال الأعمال كهدية راقية في المناسبات.",
            849.00m, "https://images.unsplash.com/photo-1583485088034-697b5bc54ccd?w=600&q=80");

        AddProduct(books, 6, "لوحة رسم رقمية Wacom Intuos Pro",
            "لوحة رسم احترافية بمساحة نشطة 22×14 سم، قلم 8192 مستوى ضغط بدون بطارية، لمس متعدد، متوافقة مع جميع برامج التصميم، واجهة USB وBluetooth، مع 5 رؤوس قلم مختلفة.",
            1299.00m, "https://images.unsplash.com/photo-1558618666-fcd25c85cd64?w=600&q=80");

        AddProduct(books, 6, "أدوات مكتبية فاخرة متكاملة",
            "طقم مكتبي 12 قطعة من الفولاذ المقاوم للصدأ والخشب: حامل أقلام، منظم أوراق، ثقالة أوراق، مقص، شريط لاصق، مفتاح خطابات، وساعة مكتبية. تصميم متناسق أنيق لبيئات العمل الراقية.",
            349.00m, "https://images.unsplash.com/photo-1497366216548-37526070297c?w=600&q=80");

        // ── 8. الرياضة واللياقة ──────────────────────────────────────────────
        AddProduct(sports, 7, "دراجة تمرين مغناطيسية ذكية",
            "دراجة ثابتة بمقاومة مغناطيسية صامتة من 16 مستوى، شاشة LCD تعرض: السرعة، المسافة، السعرات، النبض، متوافقة مع تطبيقات Zwift وPeloton عبر بلوتوث، قاعدة مضادة للانزلاق، تحمل حتى 130 كيلوجرام.",
            2499.00m, "https://images.unsplash.com/photo-1517836357463-d25dfeac3438?w=600&q=80");

        AddProduct(sports, 7, "حذاء جري نايكي Vaporfly 3",
            "حذاء جري احترافي للماراثون بلوح كربوني مدمج في النعل لتعزيز الكفاءة، رغوة ZoomX فائقة الارتداد، وزن 195 جرام فقط، سطح Flyknit للتهوية المثلى، حائز جائزة أفضل حذاء ماراثون 2024.",
            799.00m, "https://images.unsplash.com/photo-1542291026-7eec264c27ff?w=600&q=80");

        AddProduct(sports, 7, "حبال مقاومة لياقة (مجموعة 5 حبال)",
            "مجموعة 5 حبال مقاومة لاتكس طبيعي بمستويات: 10/15/20/25/30 كيلوجرام، مناسبة لتمارين كامل الجسم، مقاومة للتمزق والحرارة، مع حقيبة حمل وكتيب 50 تمريناً مصوراً.",
            149.00m, "https://images.unsplash.com/photo-1517836357463-d25dfeac3438?w=600&q=80");

        AddProduct(sports, 7, "خيمة تخييم 4 أشخاص",
            "خيمة ألومنيوم خفيفة الوزن 2.8 كيلوجرام، مقاومة للأمطار بمعامل 3000mm، تهوية مزدوجة لمنع التكاثف، تجميع في أقل من 5 دقائق، تتسع بارتياح لـ 4 أشخاص مع جيب داخلي للأمتعة.",
            599.00m, "https://images.unsplash.com/photo-1504280390367-361c6d9f38f4?w=600&q=80");

        AddProduct(sports, 7, "جهاز قياس ضغط الدم الذكي Omron",
            "جهاز قياس ضغط الدم للذراع العلوي بتقنية IntelliSense، يتصل بتطبيق الصحة عبر بلوتوث، يحفظ 120 قراءة لشخصين، يكتشف عدم انتظام نبضات القلب، شاشة كبيرة قابلة للإضاءة.",
            299.00m, "https://images.unsplash.com/photo-1559757148-5c350d0d3c56?w=600&q=80");

        await db.SaveChangesAsync();

        // ── Add ProductImages ─────────────────────────────────────────────────
        var products = await db.Products.ToListAsync();
        foreach (var product in products)
        {
            if (!string.IsNullOrWhiteSpace(product.ImageUrl))
            {
                db.ProductImages.Add(new ProductImage
                {
                    ProductId = product.Id,
                    Url       = product.ImageUrl,
                    SortOrder = 0,
                    IsPrimary = true
                });
            }
        }
        await db.SaveChangesAsync();
    }
}
