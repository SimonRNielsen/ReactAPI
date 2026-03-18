using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace ReactAPI.Controllers
{
    [Route("api/posts")]
    [ApiController]
    public class Posts : ControllerBase
    {

        private static string? currentHash;
        public static List<UserListingDTO> cachedUsers = new List<UserListingDTO>();
        public static readonly object cacheLock = new object(), postHashLock = new object(), dictLock = new object();
        private static Dictionary<string, PostDTO> cachedPosts = new Dictionary<string, PostDTO>();
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

            ReadPosts().GetAwaiter().GetResult();

        }

        [HttpGet("posts")]
        public async Task<IActionResult> GetPosts()
        {

            return Ok(await ReadPosts());

        }

        [HttpPost("addcomment")]
        public async Task<IActionResult> AddComment([FromBody] NewCommentDTO comment)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == comment.PosterID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            List<PostDTO> posts = await ReadPosts();

            PostDTO? post = posts.Find(x => comment.PostID == x.PostID);

            if (post == null)
                return Conflict(postResults[PostResults.NotFound]);

            post.Comments.Add(new CommentDTO { Comment = comment.Comment, PosterID = comment.PosterID, PostID = comment.PostID });

            SavePosts(posts);

            return Ok(postResults[PostResults.CommentAdded]);

        }

        [HttpPost("addopinion")]
        public async Task<IActionResult> AddOpinion([FromBody] OpinionDTO opinion)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == opinion.UserID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            List<PostDTO> posts = await ReadPosts();

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

            SavePosts(posts); //////////////////////////////////////////////////////////////////////////// Opinions

            return Ok(postResults[PostResults.OpinionRegistered]);

        }

        [HttpPost("newpost")]
        public async Task<IActionResult> AddPost([FromBody] CreatePostDTO newPost)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == newPost.PosterID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            List<PostDTO> posts = await ReadPosts();
            PostDTO post = new PostDTO { Post = newPost.Post, PosterID = newPost.PosterID };
            if (newPost.PictureURL != null)
                post.PictureURL = newPost.PictureURL;

            posts.Add(post);
            SavePosts(posts);

            return Ok(postResults[PostResults.Created]);

        }

        [HttpDelete("deletepost")]
        public async Task<IActionResult> DeletePost([FromBody] DeletePostDTO post)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == post.PosterID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            string? key = null;

            lock (dictLock)
                foreach (var cachedPost in cachedPosts)
                {
                    if (cachedPost.Value.PostID == post.PostID)
                    {
                        if (!cachedPost.Value.PosterID.Equals(post.PosterID))
                            return BadRequest(postResults[PostResults.NotOwner]);

                        key = cachedPost.Key;
                        break;
                    }
                }

            if (string.IsNullOrWhiteSpace(key))
                return NotFound(postResults[PostResults.NotFound]);

            await using var connection = new NpgsqlConnection(Users.database_login);
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand("DELETE FROM posts WHERE (post_id = @POST_ID AND poster_id = @POSTER_ID)", connection);
            cmd.Parameters.AddWithValue("POST_ID", post.PostID);
            cmd.Parameters.AddWithValue("POSTER_ID", post.PosterID);

            int affected = await cmd.ExecuteNonQueryAsync();

            if (affected == 0)
                return NotFound(postResults[PostResults.NotFound]);

            lock (dictLock)
                cachedPosts.Remove(key);
            ReHash();

            return Ok(postResults[PostResults.PostDeleted]);

        }

        [HttpDelete("deletecomment")]
        public async Task<IActionResult> DeleteComment([FromBody] DeleteCommentDTO comment)
        {

            lock (cacheLock)
                if (!cachedUsers.Any(x => x.ID == comment.PosterID))
                    return BadRequest(postResults[PostResults.UserNotFound]);

            PostDTO? postWithComment;
            lock (dictLock)
                postWithComment = cachedPosts.Values.FirstOrDefault(x => x.Comments.Any(y => y.CommentID == comment.CommentID));

            if (postWithComment == null)
                return Conflict(postResults[PostResults.NotFound]);

            CommentDTO? deleteThis = postWithComment.Comments.Find(x => x.CommentID == comment.CommentID);

            if (deleteThis == null)
                return Conflict(postResults[PostResults.NotFound]);

            if (!deleteThis.PosterID.Equals(comment.PosterID))
                return BadRequest(postResults[PostResults.NotOwner]);

            await using var connection = new NpgsqlConnection(Users.database_login);
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand("DELETE FROM comments WHERE comment_id = @COMMENT_ID AND commenter_id = @COMMENTER_ID", connection);
            cmd.Parameters.AddWithValue("COMMENT_ID", comment.CommentID);
            cmd.Parameters.AddWithValue("COMMENTER_ID", comment.PosterID);

            int affected = await cmd.ExecuteNonQueryAsync();

            if (affected == 0)
                return Conflict(postResults[PostResults.NotFound]);

            lock (dictLock)
                postWithComment.Comments.Remove(deleteThis);
            ReHash();

            return Ok(postResults[PostResults.CommentDeleted]);

        }


        [HttpGet("update")]
        public IActionResult SendHash()
        {

            return Ok(currentHash);

        }

        private static void SavePosts(List<PostDTO> posts) /////////////////////////////////////////////////////// Skal uddelegeres
        {

            string postsJson = JsonSerializer.Serialize(posts, new JsonSerializerOptions { WriteIndented = true });

            //System.IO.File.WriteAllText(postsFile, postsJson); ////////////////////////////////////////// Skal laves om

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(postsJson));

            currentHash = Convert.ToHexString(hash);

        }


        //private static async Task<List<PostDTO>> ReadPosts()
        //{

        //    List<PostDTO> posts = new List<PostDTO>();

        //    try
        //    {

        //        await using var connection = new NpgsqlConnection(Users.database_login);
        //        await connection.OpenAsync();

        //        await using var postsCmd = new NpgsqlCommand("SELECT * FROM posts;", connection);
        //        await using var postReader = await postsCmd.ExecuteReaderAsync();

        //        while (await postReader.ReadAsync())
        //        {

        //            PostDTO post = new PostDTO
        //            {

        //                PosterID = postReader.GetString(postReader.GetOrdinal("poster_id")),
        //                PostID = postReader.GetString(postReader.GetOrdinal("post_id")),
        //                Post = postReader.GetString(postReader.GetOrdinal("post")),
        //                PictureURL = postReader.IsDBNull(postReader.GetOrdinal("url"))
        //                    ? null
        //                    : postReader.GetString(postReader.GetOrdinal("url"))

        //            };

        //            posts.Add(post);

        //        }

        //        await connection.OpenAsync();

        //        await using var commentsCmd = new NpgsqlCommand("SELECT * FROM comments;", connection);
        //        await using var commentsReader = await commentsCmd.ExecuteReaderAsync();

        //        while (await commentsReader.ReadAsync())
        //        {

        //            CommentDTO comment = new CommentDTO
        //            {

        //                PosterID = commentsReader.GetString(commentsReader.GetOrdinal("commenter_id")),
        //                PostID = commentsReader.GetString(commentsReader.GetOrdinal("post_id")),
        //                Comment = commentsReader.GetString(commentsReader.GetOrdinal("comment")),
        //                CommentID = commentsReader.GetString(commentsReader.GetOrdinal("comment_id"))

        //            };

        //            PostDTO addComment = posts.Find(x => x.PostID == comment.PostID)!;
        //            addComment.Comments.Add(comment);

        //        }

        //        await connection.OpenAsync();

        //        await using var likesCmd = new NpgsqlCommand("SELECT * FROM likes;", connection);
        //        await using var likesReader = await likesCmd.ExecuteReaderAsync();

        //        while (await likesReader.ReadAsync())
        //        {

        //            string userID = likesReader.GetString(likesReader.GetOrdinal("user_id"));
        //            string postID = likesReader.GetString(likesReader.GetOrdinal("post_id"));

        //            PostDTO addLike = posts.Find(x => x.PostID == postID)!;
        //            addLike.Likes.Add(userID);

        //        }

        //        await connection.OpenAsync();

        //        await using var dislikesCmd = new NpgsqlCommand("SELECT * FROM dislikes;", connection);
        //        await using var dislikesReader = await dislikesCmd.ExecuteReaderAsync();

        //        while (await dislikesReader.ReadAsync())
        //        {

        //            string userID = dislikesReader.GetString(likesReader.GetOrdinal("user_id"));
        //            string postID = dislikesReader.GetString(likesReader.GetOrdinal("post_id"));

        //            PostDTO addDislike = posts.Find(x => x.PostID == postID)!;
        //            addDislike.Dislikes.Add(userID);

        //        }

        //    }
        //    catch
        //    {

        //    }

        //    return posts;

        //}

        private static async Task<List<PostDTO>> ReadPosts()
        {
            await using var connection = new NpgsqlConnection(Users.database_login);
            await connection.OpenAsync();

            string query = @"SELECT 
                p.post_id,
                p.poster_id,
                p.post,
                p.url,
   
                c.comment_id,
                c.commenter_id,
                c.comment,

                l.user_id  AS like_user,
                d.user_id  AS dislike_user

                FROM posts p
                LEFT JOIN comments c ON c.post_id = p.post_id
                LEFT JOIN likes l ON l.post_id = p.post_id
                LEFT JOIN dislikes d ON d.post_id = p.post_id
                ORDER BY p.post_id;";

            await using var cmd = new NpgsqlCommand(query, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string postId = reader.GetString(reader.GetOrdinal("post_id"));

                if (!cachedPosts.TryGetValue(postId, out var post))
                {
                    post = new PostDTO
                    {
                        PostID = postId,
                        PosterID = reader.GetString(reader.GetOrdinal("poster_id")),
                        Post = reader.GetString(reader.GetOrdinal("post")),
                        PictureURL = reader.IsDBNull(reader.GetOrdinal("url"))
                            ? null
                            : reader.GetString(reader.GetOrdinal("url"))
                    };

                    cachedPosts.Add(postId, post);
                }

                if (!reader.IsDBNull(reader.GetOrdinal("comment_id")))
                {
                    var comment = new CommentDTO
                    {
                        CommentID = reader.GetString(reader.GetOrdinal("comment_id")),
                        PosterID = reader.GetString(reader.GetOrdinal("commenter_id")),
                        PostID = postId,
                        Comment = reader.GetString(reader.GetOrdinal("comment"))
                    };

                    if (!post.Comments.Any(c => c.CommentID == comment.CommentID))
                        post.Comments.Add(comment);
                }

                if (!reader.IsDBNull(reader.GetOrdinal("like_user")))
                {
                    string userId = reader.GetString(reader.GetOrdinal("like_user"));

                    if (!post.Likes.Contains(userId))
                        post.Likes.Add(userId);
                }

                if (!reader.IsDBNull(reader.GetOrdinal("dislike_user")))
                {
                    string userId = reader.GetString(reader.GetOrdinal("dislike_user"));

                    if (!post.Dislikes.Contains(userId))
                        post.Dislikes.Add(userId);
                }
            }

            List<PostDTO> posts;

            lock (dictLock)
                posts = cachedPosts.Values.ToList();
            ReHash();

            return posts;

        }

        private static void ReHash()
        {

            List<PostDTO> posts;

            lock (dictLock)
                posts = cachedPosts.Values.ToList();

            string json = JsonSerializer.Serialize(posts);

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));

            lock (cacheLock)
                currentHash = Convert.ToHexString(hash);

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
