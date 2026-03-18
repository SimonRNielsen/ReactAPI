using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReactAPI.Controllers
{

    [ApiController]
    [Route("api/users")]
    public class Users : ControllerBase
    {

        public static readonly string database_login;
        private static string? usersHash;
        private static readonly Dictionary<UserResults, string> userResults = new Dictionary<UserResults, string>
        {

            { UserResults.Created, "User created" },
            { UserResults.FailedCreation, "Couldn't create user" },
            { UserResults.FailedLogin, "Login attempt failed" },
            { UserResults.InvalidPassOrMail, "Invalid password or email" }

        };


        static Users()
        {

            string database_user = Environment.GetEnvironmentVariable("DATABASE_USER")!;
            string database_password = Environment.GetEnvironmentVariable("DATABASE_PASSWORD")!;
            database_login =
            "Host=dpg-d6t6ng3uibrs73cnkqag-a;" +
            "Port=5432;" +
            "Database=onlymortenfans;" +
            $"Username={database_user};" +
            $"Password={database_password};" +
            "SSL Mode=Require;";

            usersHash = InitialUsersHash();

        }

        /// <summary>
        /// Endpoint to "log in"
        /// </summary>
        /// <param name="loginAttempt">Data needed to verify user</param>
        /// <returns>Response</returns>
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginDTO loginAttempt)
        {

            if (loginAttempt == null)
                return BadRequest(userResults[UserResults.FailedLogin]);

            UserReturnDTO result;

            List<User>? users = GetUsersFromDB();
            if (users == null)
                return BadRequest(userResults[UserResults.FailedCreation]);

            User? user = users.FirstOrDefault(x => x.Email.Equals(loginAttempt.Email, StringComparison.OrdinalIgnoreCase));

            if (user == null)
                return Unauthorized(userResults[UserResults.InvalidPassOrMail]);

            byte[] inputPlusSalt = Encoding.UTF8.GetBytes(loginAttempt.Password).Concat(user.Salt).ToArray();
            using SHA512 mySHA512 = SHA512.Create();
            byte[] passPlusSaltHash = mySHA512.ComputeHash(inputPlusSalt);

            if (!passPlusSaltHash.SequenceEqual(user.PasswordHashWithSalt))
                return Unauthorized(userResults[UserResults.InvalidPassOrMail]);

            result = new UserReturnDTO { Name = user.Name, Email = user.Email, ID = user.ID };

            return Ok(result);

        }

        /// <summary>
        /// Endpoint for adding/creating a new user and adding it to "users.json"
        /// </summary>
        /// <param name="newUser">User to add, contains encrypted data</param>
        /// <returns>Response</returns>
        [HttpPost("create")]
        public IActionResult CreateUser([FromBody] CreateUserDTO newUser)
        {

            if (string.IsNullOrWhiteSpace(newUser.Email) || string.IsNullOrWhiteSpace(newUser.Password) || string.IsNullOrWhiteSpace(newUser.Name))
                return BadRequest(userResults[UserResults.FailedCreation]);

            User createdUser;

            List<User>? users = GetUsersFromDB();
            if (users == null)
                return BadRequest(userResults[UserResults.FailedCreation]);

            if (users.Any(x => x.Email.Equals(newUser.Email, StringComparison.OrdinalIgnoreCase)))
                return BadRequest(userResults[UserResults.FailedCreation]);

            if (string.IsNullOrWhiteSpace(newUser.ID))
                newUser.ID = Guid.NewGuid().ToString(); //Workaround til at Morten og Goosifer kan oprettes som default

            while (users.Any(x => x.ID == newUser.ID))
                newUser.ID = Guid.NewGuid().ToString(); //Sikrer ingen kan kopiere unikke ID'er udefra eller hvis der mirakuløst skulle være en ikke unik ID

            createdUser = AddUser(newUser);

            ////////////////////////////////////////////////////////////////////////////////////// Tilføj user

            return Ok(new UserReturnDTO { Name = createdUser.Name, Email = createdUser.Email, ID = createdUser.ID });

        }

        [HttpGet("getusers")]
        public IActionResult GetUserListings()
        {

            lock (Posts.cacheLock)
                return Ok(Posts.cachedUsers); 

        }


        [HttpGet("checknewusers")]
        public IActionResult UserHash()
        {

            lock (Posts.cacheLock)
                return Ok(usersHash);

        }


        [HttpHead("ping")]
        public IActionResult Ping()
        {

            return Ok();

        }


        private static User AddUser(CreateUserDTO user)
        {

            byte[] salt = new byte[32];
            RandomNumberGenerator.Fill(salt);

            byte[] passPlusSalt = Encoding.UTF8.GetBytes(user.Password).Concat(salt).ToArray();
            using SHA512 mySHA512 = SHA512.Create();
            byte[] hashedPassWithSalt = mySHA512.ComputeHash(passPlusSalt);

            User newUser = new User()
            {

                ID = user.ID!,
                Name = user.Name,
                Email = user.Email,
                PasswordHashWithSalt = hashedPassWithSalt,
                Salt = salt,
                JoinTime = DateTime.UtcNow

            };

            lock (Posts.cacheLock)
            {
                Posts.cachedUsers.Add(new UserListingDTO { ID = newUser.ID, Name = newUser.Name, JoinTime = newUser.JoinTime });

                string createHash = JsonSerializer.Serialize(Posts.cachedUsers);
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(createHash));

                usersHash = Convert.ToHexString(hash);
            }

            return newUser;

        }

        private static string InitialUsersHash()
        {

            List<User> users = GetUsersFromDB();

            foreach (User user in users)
                Posts.cachedUsers.Add(new UserListingDTO { ID = user.ID, Name = user.Name, JoinTime = user.JoinTime, CatchPhrase = user.CatchPhrase, PictureURL = user.PictureURL });

            string createHash = JsonSerializer.Serialize(Posts.cachedUsers);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(createHash));

            return Convert.ToHexString(hash);

        }

        private static List<User> GetUsersFromDB()
        {
            return null!;
        }

    }

    /// <summary>
    /// Data storage class for saving relevant info pertinent for a specific user
    /// </summary>
    public class User
    {

        public required string ID { get; set; }


        public required string Name { get; set; }


        public required byte[] PasswordHashWithSalt { get; set; }


        public required byte[] Salt { get; set; }


        public required string Email { get; set; }


        public required DateTime JoinTime { get; set; }


        public string? CatchPhrase { get; set; }


        public string? PictureURL { get; set; }

    }

    /// <summary>
    /// Data transfer object with data needed for logging in
    /// </summary>
    public class LoginDTO
    {


        public required string Email { get; set; }


        public required string Password { get; set; }


    }

    /// <summary>
    /// Data transfer object with data pertinent for creating a new user
    /// </summary>
    public class CreateUserDTO
    {


        public required string Name { get; set; }


        public required string Email { get; set; }


        public required string Password { get; set; }


        public string? ID { get; set; }

    }

    /// <summary>
    /// Data transfer object that's used for sending data to requestee
    /// </summary>
    public class UserReturnDTO
    {


        public required string Name { get; set; }


        public required string Email { get; set; }


        public required string ID { get; set; }

    }

    public class UserListingDTO
    {


        public required string ID { get; set; }


        public required string Name { get; set; }


        public required DateTime JoinTime { get; set; }


        public string? CatchPhrase { get; set; }


        public string? PictureURL { get; set; }

    }

    public enum UserResults
    {

        Created,
        FailedCreation,
        FailedLogin,
        InvalidPassOrMail

    }

}

