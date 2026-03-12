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

        private static readonly string userFile = "tmp/users.json", resetPass;
        private static readonly object fileLock = new object();
        private static readonly Dictionary<UserResults, string> userResults = new Dictionary<UserResults, string>
        {

            { UserResults.Created, "User created" },
            { UserResults.FailedCreation, "Couldn't create user" },
            { UserResults.FailedLogin, "Login attempt failed" },
            { UserResults.InvalidPassOrMail, "Invalid password or email" }

        };


        static Users()
        {

            resetPass = "reset";

            string path = Path.GetDirectoryName(userFile)!;

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            List<User> createUsers = new List<User>();
            createUsers.Add(HashData(new CreateUserDTO { Name = "Morten", Email = "morten@oceandefender.dk", Password = "Morten1234" }));
            createUsers.Add(HashData(new CreateUserDTO { Name = "Goosifer", Email = "goosifer@oceandefender.dk", Password = "Goosifer1234" }));
            string defaultUsers = JsonSerializer.Serialize(createUsers, new JsonSerializerOptions { WriteIndented = true });

            lock (fileLock)
                System.IO.File.WriteAllText(userFile, defaultUsers);

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

                result = new UserReturnDTO { Name = user.Name, Email = user.Email };

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

            lock (fileLock)
            {

                string json = System.IO.File.ReadAllText(userFile);
                List<User> users = JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();

                if (users.Any(x => x.Email.Equals(newUser.Email, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest(userResults[UserResults.FailedCreation]);

                User createdUser = HashData(newUser);

                users.Add(createdUser);

                string updatedUsers = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(userFile, updatedUsers);

            }

            return Ok(new UserReturnDTO { Name = newUser.Name, Email = newUser.Email });

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


        [HttpHead("ping")]
        public IActionResult Ping()
        {

            return Ok();

        }


        private void ResetAction()
        {

            List<User> createUsers = new List<User>
            {

            HashData(new CreateUserDTO { Name = "Morten", Email = "morten@oceandefender.dk", Password = "Morten1234" }),
            HashData(new CreateUserDTO { Name = "Goosifer", Email = "goosifer@oceandefender.dk", Password = "Goosifer1234" })

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

                Name = user.Name,
                Email = user.Email,
                PasswordHashWithSalt = hashedPassWithSalt,
                Salt = salt,
                JoinTime = DateTime.UtcNow

            };

            return newUser;

        }

    }

    /// <summary>
    /// Data storage class for saving relevant info pertinent for a specific user
    /// </summary>
    public class User
    {

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

    }

    /// <summary>
    /// Data transfer object that's used for sending data to requestee
    /// </summary>
    public class UserReturnDTO
    {


        public required string Name { get; set; }


        public required string Email { get; set; }

    }

    public enum UserResults
    {

        Created,
        FailedCreation,
        FailedLogin,
        InvalidPassOrMail

    }

}

