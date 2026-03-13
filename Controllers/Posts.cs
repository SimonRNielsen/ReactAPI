using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReactAPI.Controllers
{
    [Route("api/posts")]
    [ApiController]
    public class Posts : ControllerBase
    {

        private static readonly string postsFile = "tmp/posts.json", resetPass;
        private static string? currentHash;
        public static List<UserListingDTO> cachedUsers = new List<UserListingDTO>();
        private static readonly object fileLock = new object();
        public static readonly object cacheLock = new object();
        private static readonly Dictionary<PostResults, string> postResults = new Dictionary<PostResults, string>
        {

            { PostResults.Created, "Post successfull" },
            { PostResults.UserNotFound, "Posting failed, user not recognized" },
            { PostResults.NotFound, "Post/comment not found" },
            { PostResults.OpinionRegistered, "Opinion registered" },
            { PostResults.CommentAdded, "Comment added to post" },
            { PostResults.PostDeleted, "Post was removed" },
            { PostResults.CommentDeleted, "Comment was removed" },
            { PostResults.NotOwner, "Not the owner of the targeted post/comment" },
            { PostResults.NotAuthenticated, "Not authorized to clear posts!" }

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

            return Ok(ReadPosts());

        }

        [HttpPost("addcomment")]
        public IActionResult AddComment([FromBody] NewCommentDTO comment)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == comment.PosterID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            List<PostDTO> posts = ReadPosts();

            PostDTO? post = posts.Find(x => comment.PostID == x.PostID);

            if (post == null)
                return Conflict(postResults[PostResults.NotFound]);

            post.Comments.Add(new CommentDTO { Comment = comment.Comment, PosterID = comment.PosterID, PostID = comment.PostID });

            SavePosts(posts);

            return Ok(postResults[PostResults.CommentAdded]);

        }

        [HttpPost("addopinion")]
        public IActionResult AddOpinion([FromBody] OpinionDTO opinion)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == opinion.UserID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            List<PostDTO> posts = ReadPosts();

            PostDTO? post = posts.Find(x => opinion.PostID == x.PostID);

            if (post == null)
                return Conflict(postResults[PostResults.NotFound]);

            if (opinion.Opinion)
            {
                if (post.Likes.Contains(opinion.UserID))
                    post.Likes.Remove(opinion.UserID);
                else
                {
                    post.Likes.Add(opinion.UserID);
                    if (post.Dislikes.Contains(opinion.UserID))
                        post.Dislikes.Remove(opinion.UserID);
                }
            }
            else
            {
                if (post.Dislikes.Contains(opinion.UserID))
                    post.Dislikes.Remove(opinion.UserID);
                else
                {
                    post.Dislikes.Add(opinion.UserID);
                    if (post.Likes.Contains(opinion.UserID))
                        post.Likes.Remove(opinion.UserID);
                }
            }

            SavePosts(posts);

            return Ok(postResults[PostResults.OpinionRegistered]);

        }

        [HttpPost("newpost")]
        public IActionResult AddPost([FromBody] CreatePostDTO newPost)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == newPost.PosterID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            List<PostDTO> posts = ReadPosts();
            PostDTO post = new PostDTO { Post = newPost.Post, PosterID = newPost.PosterID };
            if (newPost.PictureURL != null)
                post.PictureURL = newPost.PictureURL;

            posts.Add(post);
            SavePosts(posts);

            return Ok(postResults[PostResults.Created]);

        }

        [HttpDelete("deletepost")]
        public IActionResult DeletePost([FromBody] DeletePostDTO post)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == post.PosterID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            List<PostDTO> posts = ReadPosts();

            PostDTO? deleteThis = posts.Find(x => x.PostID == post.PostID);

            if (deleteThis == null)
                return Conflict(postResults[PostResults.NotFound]);

            if (!deleteThis.PosterID.Equals(post.PosterID))
                return BadRequest(postResults[PostResults.NotOwner]);

            posts.Remove(deleteThis);
            SavePosts(posts);

            return Ok(postResults[PostResults.PostDeleted]);

        }

        [HttpDelete("deletecomment")]
        public IActionResult DeleteComment([FromBody] DeleteCommentDTO comment)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == comment.PosterID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            List<PostDTO> posts = ReadPosts();

            PostDTO? postWithComment = posts.Find(x => x.Comments.Any(y => y.CommentID == comment.CommentID));

            if (postWithComment == null)
                return Conflict(postResults[PostResults.NotFound]);

            CommentDTO? deleteThis = postWithComment.Comments.Find(x => x.CommentID == comment.CommentID);

            if (deleteThis == null)
                return Conflict(postResults[PostResults.NotFound]);

            if (!deleteThis.PosterID.Equals(comment.PosterID))
                return BadRequest(postResults[PostResults.NotOwner]);

            postWithComment.Comments.Remove(deleteThis);
            SavePosts(posts);

            return Ok(postResults[PostResults.CommentDeleted]);

        }

        [HttpDelete("clearposts")]
        public IActionResult ClearPosts([FromBody] string pass)
        {

            if (string.IsNullOrEmpty(pass) || pass != resetPass)
                return Unauthorized(postResults[PostResults.NotAuthenticated]);

            CreateDefaultPosts();

            return NoContent();

        }

        [HttpGet("update")]
        public IActionResult SendHash()
        {

            return Ok(currentHash);

        }


        private static void CreateDefaultPosts()
        {

            List<PostDTO> defaultPosts = new List<PostDTO>();

            PostDTO mortenPost = new PostDTO { PosterID = "a7b9e4d1-3c2f-4d8a-9e5b-6f1c2d3e4a90", Post = "I Like dead geese and i cannot lie!", PictureURL = "https://nypost.com/wp-content/uploads/sites/2/2024/09/viral-pic-man-lugging-around-89661423.jpg?w=1024" }; //Morten
            mortenPost.Likes.Add("a7b9e4d1-3c2f-4d8a-9e5b-6f1c2d3e4a90"); //Morten upvote
            mortenPost.Dislikes.Add("d3f1c2a4-8b6e-4a91-9c2d-1f7e5a6b8c30"); //Goosifer downvote
            CommentDTO goosiferComment = new CommentDTO { PosterID = "d3f1c2a4-8b6e-4a91-9c2d-1f7e5a6b8c30", PostID = mortenPost.PostID, Comment = "Screw you Morten ..!.." }; //Goosifer
            mortenPost.Comments.Add(goosiferComment);

            defaultPosts.Add(mortenPost);
            SavePosts(defaultPosts);

        }


        private static void SavePosts(List<PostDTO> posts)
        {

            string postsJson = JsonSerializer.Serialize(posts, new JsonSerializerOptions { WriteIndented = true });

            lock (fileLock)
            {

                System.IO.File.WriteAllText(postsFile, postsJson);

                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(postsJson));

                currentHash = Convert.ToHexString(hash);

            }

        }


        private static List<PostDTO> ReadPosts()
        {

            string json;
            lock (fileLock)
                json = System.IO.File.ReadAllText(postsFile);
            List<PostDTO> posts = JsonSerializer.Deserialize<List<PostDTO>>(json) ?? new List<PostDTO>();

            return posts;

        }

    }

    public enum PostResults
    {

        Created,
        FailedCreation,
        UserNotFound,
        NotFound,
        OpinionRegistered,
        CommentAdded,
        CommentDeleted,
        PostDeleted,
        NotOwner,
        NotAuthenticated

    }

    /// <summary>
    /// Data transfer object that's used for sending data to requestee
    /// </summary>
    public class PostDTO
    {

        public string PostID { get; set; } = Guid.NewGuid().ToString();

        public required string PosterID { get; set; }

        public required string Post { get; set; }

        public string? PictureURL { get; set; }

        public List<CommentDTO> Comments { get; set; } = new List<CommentDTO>();

        public List<string> Likes { get; set; } = new List<string>();

        public List<string> Dislikes { get; set; } = new List<string>();

    }

    public class CreatePostDTO
    {

        public required string PosterID { get; set; }

        public required string Post { get; set; }

        public string? PictureURL { get; set; }

    }

    public class CommentDTO
    {

        public string CommentID { get; set; } = Guid.NewGuid().ToString();

        public required string PostID { get; set; }

        public required string PosterID { get; set; }

        public required string Comment { get; set; }

    }

    public class NewCommentDTO
    {

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

        public required string CommentID { get; set; }

        public required string PosterID { get; set; }

    }

    public class OpinionDTO
    {

        public required string PostID { get; set; }

        public required string UserID { get; set; }

        public required bool Opinion { get; set; }

    }

}
