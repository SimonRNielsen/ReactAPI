using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ReactAPI.Controllers
{
    [Route("api/posts")]
    [ApiController]
    public class Posts : ControllerBase
    {

        private static readonly string postsFile = "tmp/posts.json", resetPass;
        private static readonly object fileLock = new object();
        private static readonly Dictionary<PostResults, string> userResults = new Dictionary<PostResults, string>
        {

            { PostResults.Created, "Post successfull" },
            { PostResults.FailedCreation, "Posting failed" }

        };

        static Posts()
        {

            resetPass = "clear";
            string path = Path.GetDirectoryName(postsFile)!;

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            CreateDefaultPosts();

        }

        [HttpGet("posts")]
        public IActionResult GetPosts() 
        {

            string json;
            lock (fileLock)
                json = System.IO.File.ReadAllText(postsFile);
            List<PostDTO> posts = JsonSerializer.Deserialize<List<PostDTO>>(json) ?? new List<PostDTO>();

            return Ok(posts);

        }

        [HttpPost("addcomment")]
        public IActionResult AddComment([FromBody] CommentDTO comment) 
        { 
            
            return BadRequest("Not yet implemented"); 
        
        }

        [HttpPost("addopinion")]
        public IActionResult AddOpinion([FromBody] string id)
        {

            return BadRequest("Not yet implemented");

        }

        [HttpPost("newpost")]
        public IActionResult AddPost([FromBody] CreatePostDTO post)
        {

            string json;
            lock (fileLock)
                json = System.IO.File.ReadAllText(postsFile);
            List<PostDTO> posts = JsonSerializer.Deserialize<List<PostDTO>>(json) ?? new List<PostDTO>();
            

            return BadRequest("Not yet implemented");

        }

        [HttpDelete("deletepost")]
        public IActionResult DeletePost([FromBody] DeletePostDTO post)
        {

            return BadRequest("Not yet implemented");

        }

        [HttpDelete("deletecomment")]
        public IActionResult DeleteComment([FromBody] DeleteCommentDTO comment)
        {

            return BadRequest("Not yet implemented");

        }

        [HttpDelete("clearposts")]
        public IActionResult ClearPosts()
        {

            CreateDefaultPosts();

            return Ok();

        }

        private static void CreateDefaultPosts()
        {

            List<PostDTO> defaultPosts = new List<PostDTO>();

            PostDTO mortenPost = new PostDTO { PosterID = "a7b9e4d1-3c2f-4d8a-9e5b-6f1c2d3e4a90", Post = "I Like dead geese and i cannot lie" }; //Morten
            mortenPost.Likes.Add("a7b9e4d1-3c2f-4d8a-9e5b-6f1c2d3e4a90");
            mortenPost.Dislikes.Add("d3f1c2a4-8b6e-4a91-9c2d-1f7e5a6b8c30");
            CommentDTO goosiferComment = new CommentDTO { PosterID = "d3f1c2a4-8b6e-4a91-9c2d-1f7e5a6b8c30", PostID = mortenPost.PostID, Comment = "Damn you Morten!" }; //Goosifer

            mortenPost.Comments.Add(goosiferComment);
            defaultPosts.Add(mortenPost);

            string postsJson = JsonSerializer.Serialize(defaultPosts, new JsonSerializerOptions { WriteIndented = true });

            lock (fileLock)
                System.IO.File.WriteAllText(postsFile, postsJson);

        }

    }

    public enum PostResults
    {

        Created,
        FailedCreation

    }

    /// <summary>
    /// Data transfer object that's used for sending data to requestee
    /// </summary>
    public class PostDTO
    {

        public string PostID { get; set; } = Guid.NewGuid().ToString();

        public required string PosterID { get; set; }

        public required string Post { get; set; }

        public List<CommentDTO> Comments { get; set; } = new List<CommentDTO>();

        public List<string> Likes { get; set; } = new List<string>();

        public List<string> Dislikes { get; set; } = new List<string>();

    }

    public class CreatePostDTO
    {

        public required string PosterID { get; set; }

        public required string Post { get; set; }

    }

    public class CommentDTO
    {

        public string CommentID { get; set; } = Guid.NewGuid().ToString();

        public required string PostID { get; set; }

        public required string PosterID { get; set; }

        public required string Comment { get; set; }

    }

    public class DeletePostDTO
    {

        public required string PostID { get; set; }

        public required string PosterID { get; set; }

    }

    public class DeleteCommentDTO
    {

        public required string PostID { get; set; }

        public required string PosterID { get; set; }

    }

}
