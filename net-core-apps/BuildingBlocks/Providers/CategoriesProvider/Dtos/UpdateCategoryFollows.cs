namespace CategoriesProvider.Dtos
{
    public class UpdateCategoryFollowsDto
    {
        public int[] Ids { get; set; }
        public string[] Names { get; set; }
        public bool Increment { get; set; }

        public UpdateCategoryFollowsDto(int[] ids, string[] names, bool increment)
        {
            Ids = ids;
            Names = names;
            Increment = increment;
        }
    }
}
