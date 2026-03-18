using Microsoft.AspNetCore.Mvc;
using Npgsql;
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
            { UserResults.InvalidPassOrMail, "Invalid password or email" },
            { UserResults.ProfileUpdated, "Profile updated" },
            { UserResults.UpdateFailed, "Profile update failed" }

        };


        static Users()
        {

            string database_user = Environment.GetEnvironmentVariable("DATABASE_USER")!;
            string database_password = Environment.GetEnvironmentVariable("DATABASE_PASSWORD")!;
            //string database_internalURL = Environment.GetEnvironmentVariable("DATABASE_URL")!;
            //string database_path = Environment.GetEnvironmentVariable("DATABASE_PATH")!;
            database_login =
            $"Host=dpg-d6t6ng3uibrs73cnkqag-a;" +
            "Port=5432;" +
            $"Database=onlymortenfans;" +
            $"Username={database_user};" +
            $"Password={database_password};" +
            "SSL Mode=Require;";

            InitialUsersHash().GetAwaiter().GetResult();

        }

        /// <summary>
        /// Endpoint to "log in"
        /// </summary>
        /// <param name="loginAttempt">Data needed to verify user</param>
        /// <returns>Response</returns>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDTO loginAttempt)
        {

            if (loginAttempt == null)
                return BadRequest(userResults[UserResults.FailedLogin]);

            UserReturnDTO result;

            List<User>? users = await GetUsersFromDB();
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
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDTO newUser)
        {

            if (string.IsNullOrWhiteSpace(newUser.Email) || string.IsNullOrWhiteSpace(newUser.Password) || string.IsNullOrWhiteSpace(newUser.Name))
                return BadRequest(userResults[UserResults.FailedCreation]);

            User createdUser;

            List<User>? users = await GetUsersFromDB();
            if (users == null)
                return BadRequest(userResults[UserResults.FailedCreation]);

            if (users.Any(x => x.Email.Equals(newUser.Email, StringComparison.OrdinalIgnoreCase)))
                return BadRequest(userResults[UserResults.FailedCreation]);

            if (string.IsNullOrWhiteSpace(newUser.ID))
                newUser.ID = Guid.NewGuid().ToString(); //Workaround til at Morten og Goosifer kan oprettes som default

            while (users.Any(x => x.ID == newUser.ID))
                newUser.ID = Guid.NewGuid().ToString(); //Sikrer ingen kan kopiere unikke ID'er udefra eller hvis der mirakuløst skulle være en ikke unik ID

            createdUser = AddUser(newUser);

            await using var connection = new NpgsqlConnection(database_login);
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand("INSERT INTO users (id, name, email, salt, hashedpw, datejoined) VALUES (@ID, @NAME, @EMAIL, @SALT, @HASHEDPW, @DATEJOINED)", connection);
            cmd.Parameters.AddWithValue("ID", createdUser.ID);
            cmd.Parameters.AddWithValue("NAME", createdUser.Name);
            cmd.Parameters.AddWithValue("EMAIL", createdUser.Email);
            cmd.Parameters.AddWithValue("SALT", createdUser.Salt);
            cmd.Parameters.AddWithValue("HASHEDPW", createdUser.PasswordHashWithSalt);
            cmd.Parameters.AddWithValue("DATEJOINED", createdUser.JoinTime);

            await cmd.ExecuteNonQueryAsync();

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

        [HttpPost("updateprofile")]
        public async Task<IActionResult> UpdateUser([FromBody] ProfileUpdateDTO profileUpdate)
        {

            await using var connection = new NpgsqlConnection(database_login);
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand("UPDATE users SET name = @NAME, catchphrase = @CATCHPHRASE, picture_url = @PICTURE WHERE id = @ID", connection);
            cmd.Parameters.AddWithValue("NAME", profileUpdate.Name);
            cmd.Parameters.AddWithValue("CATCHPHRASE", profileUpdate.CatchPhrase);
            cmd.Parameters.AddWithValue("PICTURE", profileUpdate.PictureURL);
            cmd.Parameters.AddWithValue("ID", profileUpdate.ID);

            await cmd.ExecuteNonQueryAsync();

            return Ok(userResults[UserResults.ProfileUpdated]);

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

        private static async Task InitialUsersHash()
        {

            List<User> users = await GetUsersFromDB();

            Posts.cachedUsers.Clear();

            foreach (User user in users)
                Posts.cachedUsers.Add(new UserListingDTO { ID = user.ID, Name = user.Name, JoinTime = user.JoinTime, CatchPhrase = user.CatchPhrase, PictureURL = user.PictureURL });

            string createHash = JsonSerializer.Serialize(Posts.cachedUsers);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(createHash));

            lock (Posts.cacheLock)
                usersHash = Convert.ToHexString(hash);

        }


        private static async Task<List<User>> GetUsersFromDB()
        {

            List<User> users = new List<User>();

            await using var connection = new NpgsqlConnection(database_login);
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand("SELECT * FROM users;", connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                User user = new User
                {

                    ID = reader.GetString(reader.GetOrdinal("id")),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Email = reader.GetString(reader.GetOrdinal("email")),
                    Salt = (byte[])reader["salt"],
                    PasswordHashWithSalt = (byte[])reader["hashedpw"],
                    JoinTime = reader.GetDateTime(reader.GetOrdinal("datejoined")),
                    CatchPhrase = reader.IsDBNull(reader.GetOrdinal("catchphrase"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("catchphrase")),
                    PictureURL = reader.IsDBNull(reader.GetOrdinal("picture_url"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("picture_url"))

                };

                users.Add(user);
            }

            return users;

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

    public class ProfileUpdateDTO
    {


        public required string ID { get; set; }


        public required string Name { get; set; }


        public required string CatchPhrase { get; set; }


        public required string PictureURL { get; set; }

    }

    public enum UserResults
    {

        Created,
        FailedCreation,
        FailedLogin,
        InvalidPassOrMail,
        ProfileUpdated,
        UpdateFailed

    }

}

