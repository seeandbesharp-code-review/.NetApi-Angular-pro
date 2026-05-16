using System.Linq.Expressions;
using ChineseSaleApi.Dto;
using ChineseSaleApi.Models;
using ChineseSaleApi.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using StackExchange.Redis;



namespace ChineseSaleApi.Services
{
    public class GiftService : IGiftService
    {
        private readonly IGiftRepository _repo;
        private readonly IDatabase _redis; // הוספה

        public GiftService(IGiftRepository repo, IConnectionMultiplexer redis)
        {
            _repo = repo;
            // עכשיו הקומפיילר ידע מה זה "redis"
            _redis = redis.GetDatabase();
        }

        ////get all gifts
        //public async Task<List<GiftDto>> GetAllGiftsAsync()
        //{
        //    var gifts = await _repo.GetAllGiftsAsync();
        //    return gifts.Select(d => new GiftDto
        //    {
        //        Name = d.Name,
        //        TicketCost = d.TicketCost,
        //        Description = d.Description,
        //        PictureUrl = d.PictureUrl,
        //        Id = d.Id,
        //        Category = new CategoryDto
        //        {
        //            Id = d.Category.Id,
        //            Name = d.Category?.Name ?? string.Empty
        //        },
        //        Donor = new DonorDto
        //        {
        //            Id = d.Donor.Id,
        //            Name = d.Donor.Name,
        //            Email = d.Donor.Email
        //        }

        //    }).ToList();
        //}

        //get all gifts
        public async Task<List<GiftDto>> GetAllGiftsAsync()
        {
            string cacheKey = "all_gifts_cache";

            // 1. ננסה להביא מה-Redis
            var cachedGifts = await _redis.StringGetAsync(cacheKey);
            if (!cachedGifts.IsNull)
            {
                // אם נמצא ב-cache, נהפוך חלוזרה מרשימת טקסט לרשימת אובייקטים
                var cachedGiftDtos = JsonSerializer.Deserialize<List<GiftDto>>(cachedGifts!);
                if (cachedGiftDtos != null)
                    return cachedGiftDtos;
            }

            // 2. אם לא נמצא, נלך למסד הנתונים (הקוד המקורי שלך)
            var gifts = await _repo.GetAllGiftsAsync();
            var giftsDto = gifts.Select(d => new GiftDto
            {
                Name = d.Name,
                TicketCost = d.TicketCost,
                Description = d.Description,
                PictureUrl = d.PictureUrl,
                Id = d.Id,
                Category = new CategoryDto
                {
                    Id = d.Category.Id,
                    Name = d.Category?.Name ?? string.Empty
                },
                Donor = new DonorDto
                {
                    Id = d.Donor.Id,
                    Name = d.Donor.Name,
                    Email = d.Donor.Email
                }
            }).ToList();

            // 3. נשמור ב-Redis ל-10 דקות כדי שבפעם הבאה יהיה מהיר
            await _redis.StringSetAsync(cacheKey, JsonSerializer.Serialize(giftsDto), TimeSpan.FromMinutes(10));


            return giftsDto;
        }

    //add gift
    public async Task AddGiftAsync(AddGiftDto giftDto)
        {
            if (!await _repo.DonorExistsAsync(giftDto.DonorId))
                throw new KeyNotFoundException("Donor not found");

            if (!await _repo.CategoryExistsAsync(giftDto.CategoryId))
                throw new KeyNotFoundException("Category not found");

            var gift = new Gift
            {
                Name = giftDto.Name,
                TicketCost = giftDto.TicketCost,
                Description = giftDto.Description,
                CategoryId = giftDto.CategoryId,
                DonorId = giftDto.DonorId
            };

            await _repo.AddGiftAsync(gift);
            await _redis.KeyDeleteAsync("all_gifts_cache");

        }

        //update gift
        public async Task UpdateGiftAsync(int id, AddGiftDto giftDto)
        {
            var gift = await _repo.GetGiftByIdAsync(id);
            if (gift == null)
                throw new KeyNotFoundException($"Gift with ID {id} not found.");

            if (!await _repo.DonorExistsAsync(giftDto.DonorId))
                throw new KeyNotFoundException("Donor not found");

            if (!await _repo.CategoryExistsAsync(giftDto.CategoryId))
                throw new KeyNotFoundException("Category not found");

            gift.Name = giftDto.Name;
            gift.TicketCost = giftDto.TicketCost;
            gift.Description = giftDto.Description;
            gift.CategoryId = giftDto.CategoryId;
            gift.DonorId = giftDto.DonorId;

            await _repo.SaveChangesAsync();
            await _redis.KeyDeleteAsync("all_gifts_cache");

        }

        //delete gift
        public async Task DeleteGiftByIdAsync(int id)
        {

            var exists = await _repo.GiftFindAsync(id);
            if (exists == null)
                throw new KeyNotFoundException($"Gift with ID {id} not found.");

            await _repo.DeleteGiftAsync(exists);
            await _redis.KeyDeleteAsync("all_gifts_cache");


        }

        //add picture to gift
        public async Task AddPictureToGiftAsync(int giftId, IFormFile? picture)
        {
            var gift = await _repo.GiftFindAsync(giftId);

            if (gift == null)
                throw new KeyNotFoundException($"Gift with ID {giftId} not found.");

            if (picture == null || picture.Length == 0)
                throw new ArgumentException("No file uploaded.");
            // שמירה לתיקייה (wwwroot/images)
            var ext = Path.GetExtension(picture.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";

            var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");
            Directory.CreateDirectory(folder);

            var fullPath = Path.Combine(folder, fileName);
            using var stream = new FileStream(fullPath, FileMode.Create);
            await picture.CopyToAsync(stream);

            var pictureUrl = $"/images/{fileName}";

            await _repo.AddPictureToGiftAsync(gift, pictureUrl);
        }

        //get donor by gift id
        public async Task<DonorDto> GetDonorByGiftIdAsync(int giftId)
        {
            var donor = await _repo.GetDonorByGiftIdAsync(giftId);
            if (donor == null)
                throw new KeyNotFoundException($"Donor for Gift ID {giftId} not found.");
            return new DonorDto
            {
                Id = donor.Id,
                Name = donor.Name,
                Email = donor.Email
            };
        }

        //search gifts
        public async Task<List<GiftDto>> SearchGiftsAsync(SearchGiftDto parameter)
        {
            var gifts = await _repo.SearchGiftsAsync(parameter);
            if (gifts == null || !gifts.Any())
                return [];
            return gifts.Select(d => new GiftDto
            {
                Name = d.Name,
                TicketCost = d.TicketCost,
                Description = d.Description,
                PictureUrl = d.PictureUrl,
                Id = d.Id,
                Category = new CategoryDto

                {
                    Id = d.Id,
                    Name = d.Category?.Name ?? string.Empty
                },
                Donor = new DonorDto
                {
                    Id = d.Donor.Id,
                    Name = d.Donor.Name,
                    Email = d.Donor.Email
                }
            }).ToList();
        }
    }
}


