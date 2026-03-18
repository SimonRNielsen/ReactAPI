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

            lock (fileLock)
            {

                string json = System.IO.File.ReadAllText(userFile);
                List<User> users = JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();

                User? user = users.FirstOrDefault(x => x.Email.Equals(loginAttempt.Email, StringComparison.OrdinalIgnoreCase));

                if (user == null)
                    return Unauthorized(userResults[UserResults.InvalidPassOrMail]);

                byte[] inputPlusSalt = Encoding.UTF8.GetBytes(loginAttempt.Password).Concat(user.Salt).ToArray();
                using SHA256 mySHA256 = SHA256.Create();
                byte[] passPlusSaltHash = mySHA256.ComputeHash(inputPlusSalt);

                if (!passPlusSaltHash.SequenceEqual(user.PasswordHashWithSalt))
                    return Unauthorized(userResults[UserResults.InvalidPassOrMail]);

                result = new UserReturnDTO { Name = user.Name, Email = user.Email, ID = user.ID };

            }

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

            lock (fileLock)
            {

                string json = System.IO.File.ReadAllText(userFile);
                List<User> users = JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();

                if (users.Any(x => x.Email.Equals(newUser.Email, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest(userResults[UserResults.FailedCreation]);

                if (string.IsNullOrWhiteSpace(newUser.ID))
                    newUser.ID = Guid.NewGuid().ToString(); //Workaround til at Morten og Goosifer kan oprettes som default

                while (users.Any(x => x.ID == newUser.ID))
                    newUser.ID = Guid.NewGuid().ToString(); //Sikrer ingen kan kopiere unikke ID'er udefra eller hvis der mirakuløst skulle være en ikke unik ID

                createdUser = HashData(newUser);

                users.Add(createdUser);

                string updatedUsers = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(userFile, updatedUsers);

            }

            return Ok(new UserReturnDTO { Name = createdUser.Name, Email = createdUser.Email, ID = createdUser.ID! });

        }

        /// <summary>
        /// Testing tool to see contents of the users.json file
        /// </summary>
        /// <returns>All "users"</returns>
        [HttpGet("testreader")]
        public IEnumerable<User> Get()
        {

            string json;
            lock (fileLock)
                json = System.IO.File.ReadAllText(userFile);
            List<User> users = JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
            return users;

        }

        /// <summary>
        /// Deletes the contents of "users.json"
        /// </summary>
        /// <returns>Response</returns>
        [HttpDelete("clear")]
        public IActionResult DeleteUsers([FromBody] string text)
        {

            if (text != resetPass)
                return BadRequest();

            ResetAction();

            return NoContent();

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


        private void ResetAction()
        {

            List<User> createUsers = new List<User>
            {

            HashData(new CreateUserDTO { Name = "Morten", Email = "morten@oceandefender.dk", Password = "Morten1234", ID = "a7b9e4d1-3c2f-4d8a-9e5b-6f1c2d3e4a90" }),
            HashData(new CreateUserDTO { Name = "Goosifer", Email = "goosifer@oceandefender.dk", Password = "Goosifer1234", ID = "d3f1c2a4-8b6e-4a91-9c2d-1f7e5a6b8c30" })

            };
            string defaultUsers = JsonSerializer.Serialize(createUsers, new JsonSerializerOptions { WriteIndented = true });

            lock (fileLock)
                System.IO.File.WriteAllText(userFile, defaultUsers);

        }


        private static User HashData(CreateUserDTO user)
        {

            byte[] salt = new byte[16];
            RandomNumberGenerator.Fill(salt);

            byte[] passPlusSalt = Encoding.UTF8.GetBytes(user.Password).Concat(salt).ToArray();
            using SHA256 mySHA256 = SHA256.Create();
            byte[] hashedPassWithSalt = mySHA256.ComputeHash(passPlusSalt);

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
                Posts.cachedUsers.Add(new UserListingDTO { ID = newUser.ID, Name = user.Name });

                string createHash = JsonSerializer.Serialize(Posts.cachedUsers);
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(createHash));

                usersHash = Convert.ToHexString(hash);
            }

            return newUser;

        }

    }

    /// <summary>
    /// Data storage class for saving relevant info pertinent for a specific user
    /// </summary>
    public class User
    {

        public string? ID { get; set; }

        public required string Name { get; set; }


        public required byte[] PasswordHashWithSalt { get; set; }


        public required byte[] Salt { get; set; }


        public required string Email { get; set; }


        public required DateTime JoinTime { get; set; }

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

    }

    public enum UserResults
    {

        Created,
        FailedCreation,
        FailedLogin,
        InvalidPassOrMail

    }

}

