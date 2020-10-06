using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace API.Resources
{
    /// <summary>
    ///     The view model result of UserProject
    /// </summary>
    public class UserProjectResource
    {
        /// <summary>
        ///     Get or Set Id of a userProjectService Resource Result
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        ///     This gets or sets the project
        /// </summary>
        public Project Project { get; set; }

        /// <summary>
        ///     This gets or sets the user
        /// </summary>
        public User User { get; set; }
        /// <summary>
        ///     This gets or sets the userid
        /// </summary>
        public int UserId { get; set; }
    }
}