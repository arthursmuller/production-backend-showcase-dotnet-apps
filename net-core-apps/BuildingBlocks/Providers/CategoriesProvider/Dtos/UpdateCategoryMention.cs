namespace CategoriesProvider.Dtos
{
    public class UpdateCategoryMentionsDto
    {
        public string[] NamesToAdd { get; set; }
        public string[] NamesToRemove { get; set; }
        public UpdateCategoryMentionsDto(string[] namesToAdd = null, string[] namesToRemove = null)
        {
            NamesToAdd = namesToAdd;
            NamesToRemove = namesToRemove;
        }
    }
}
