using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Security.Policy;
using System.Web;

namespace CodeFirstWebFramework {
	/// <summary>
	/// Class to provide Admin functions - called from Admin AppModule.
	/// </summary>
	public class AdminHelper {
		AppModule module;

		/// <summary>
		/// Create AdminHelper for supplied module
		/// </summary>
		public AdminHelper(AppModule module) {
			this.module = module;
		}

		/// <summary>
		/// Create form to edit settings
		/// </summary>
		public Form EditSettings() {
			Form form = new Form(module, module.Settings.GetType()) {
				Data = module.Settings.ToJToken()
			};
			IDirectoryInfo skinFolder = module.DirectoryInfo("skin");
			form["Skin"].MakeSelectable(skinFolder.Content("*.css")
						.Where(f => skinFolder.Content(f.Name + ".js").Any())
						.Select(f => new JObject().AddRange("value", f.Name)));
			form.Add(new FieldAttribute() {
				Data = "AppVersion",
				Type = "string"
			});
			return form;
		}

		/// <summary>
		/// Update settings
		/// </summary>
		public AjaxReturn EditSettingsSave(JObject json) {
			module.Database.BeginTransaction();
			json["idSettings"] = 1;
			module.Database.Update("Settings", json);
			module.ReloadSettings();
			module.Database.Commit();
			return new AjaxReturn() { message = "Settings saved", redirect = "/Admin" };
		}

		/// <summary>
		/// Display current or select batch details
		/// </summary>
		[Auth(AccessLevel.Any)]
		public void Batch() {
		}

		/// <summary>
		/// Display all running batch jobs
		/// </summary>
		public ListForm BatchJobs() {
			ListForm form = new ListForm(module, typeof(AppModule.BatchJob.BatchJobItem));
			form.Data = module.BatchJobItems.ToList();
			return form;
		}

		/// <summary>
		/// Return the status of the given batch job.
		/// </summary>
		[Auth(AccessLevel.Any)]
		public AjaxReturn BatchStatus(int id) {
			AjaxReturn result = new AjaxReturn();
			AppModule.BatchJob batch = AppModule.GetBatchJob(id);
			if (batch == null) {
				module.Log("Invalid batch id");
				result.error = "Invalid batch id";
			} else {
				if (batch == null) {
					module.Log("Invalid batch id");
					result.error = "Invalid batch id";
				} else {
					module.Log("Batch {0}:{1}%:{2}", batch.Id, batch.PercentComplete, batch.Status);
					result.data = batch;
					if (batch.Finished) {
						result.error = batch.Error;
						result.redirect = batch.RedirectWithMessage();
						module.Log("Batch finished - redirecting to {0}", batch.Redirect);
					}
				}
			}
			return result;
		}

		/// <summary>
		/// Backup the database
		/// </summary>
		public void Backup() {
			try {
				module.Database.Logging = false;
				module.Database.BeginTransaction();
				DateTime now = Utils.Now;
				JObject result = new JObject().AddRange("BackupDate", now.ToString("yyyy-MM-dd HH:mm:ss"));
				foreach (string name in module.Database.TableNames) {
					if (name != "Session")
						result.Add(name, module.Database.Query("SELECT * FROM " + name));
				}
				module.Response.AddHeader("Content-disposition", "attachment; filename=Backup-" + now.ToString("yyyy-MM-dd-HH-mm-ss") + ".json");
				module.WriteResponse(Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented), "application/json", System.Net.HttpStatusCode.OK);
			} finally {
				module.Database.Logging = true;
			}
		}

		/// <summary>
		/// Restore the database
		/// </summary>
		public void Restore() {
			if (module.PostParameters != null && module.PostParameters["file"] != null) {
				new AppModule.BatchJob(module, delegate () {
					module.Batch.Status = "Loading new data";
					UploadedFile data = module.PostParameters.As<UploadedFile>("file");
					try {
						module.Database.Logging = false;
						module.Database.BeginTransaction();
						JObject d = data.Content.JsonTo<JObject>();
						List<Table> tables = module.Database.TableNames.Select(n => module.Database.TableFor(n)).ToList();
						module.Batch.Records = tables.Count * 4;
						foreach (Table t in tables) {
							if (d[t.Name] != null) {
								module.Batch.Records += ((JArray)d[t.Name]).Count;
							}
						}
						module.Batch.Status = "Deleting existing data";
						TableList orderedTables = new TableList(tables);
						foreach (Table t in orderedTables) {
							if (t.Name != "Session") {
								module.Database.Execute("DELETE FROM " + t.Name);
								module.Batch.Record += 4;
							}
						}
						module.Database.Logging = false;
						foreach (Table t in orderedTables.Reverse<Table>()) {
							if (t.Name != "Session" && d[t.Name] != null) {
								module.Batch.Status = "Restoring " + t.Name + " data";
								foreach (JObject record in (JArray)d[t.Name]) {
									module.Database.Insert(t.Name, record);
									module.Batch.Record++;
								}
							}
						}
						module.Batch.Status = "Checking database version";
						module.Database.Upgrade();
						module.Database.Commit();
						module.Batch.Status = "Compacting database";
						module.Database.Clean();
						module.ReloadSettings();
						module.Batch.Status = module.Message = "Database restored successfully";
					} finally {
						module.Database.Logging = true;
					}
				});
			}
		}

		/// <summary>
		/// Create datatable to list users
		/// </summary>
		public DataTableForm Users() {
			module.InsertMenuOption(new MenuOption("New User", "/admin/EditUser?id=0&from=%2Fadmin%2Fusers"));
			Table t = module.Database.TableFor("User");
			DataTableForm f = new DataTableForm(module, t.Type) {
				Select = "/admin/edituser.html"
			};
			f.Remove("Password");
			AccessLevel levels = module.Server.NamespaceDef.GetAccessLevel();
			f["AccessLevel"].MakeSelectable(levels.Select());
			return f;
		}

		/// <summary>
		/// List users for Users form
		/// </summary>
		public JObjectEnumerable UsersListing() {
			return module.Database.Query("SELECT * FROM User ORDER BY Login");
		}

		/// <summary>
		/// Create form to edit an individual user
		/// </summary>
		public void EditUser(int id) {
			User user = module.Database.Get<User>(id);
			user.Password = "";
			HeaderDetailForm f = new HeaderDetailForm(module, new Form(module, user.GetType()), 
				new ListForm(module, typeof(Permission), true, "Module", "Function", "FunctionAccessLevel"));
			f.Header["Password"].MaxLength = 512;
            f.Header["Password"].Options["size"] = 45;
			f.Header.Insert(f.Header.IndexOf("Password") + 1, new FieldAttribute() {
				Data = "RepeatPassword",
				Type = "passwordInput",
				MaxLength = 512
			});
			f.Header["RepeatPassword"].Options["size"] = 45;
			AccessLevel levels = module.Server.NamespaceDef.GetAccessLevel();
			f.Header["AccessLevel"].MakeSelectable(levels.Select(module.UserAccessLevel));
			f.Detail["FunctionAccessLevel"].MakeSelectable(new JObject[] { new JObject().AddRange("id", AccessLevel.Any, "value", "User Setting") }.Concat(levels.Select(module.UserAccessLevel)));
			f.Detail.Remove("Method");
			if (!module.Server.NamespaceDef.OldAuth)
				f.Detail.Remove("Module");
			f.CanDelete = id > 1 || (id == 1 && module.Database.QueryOne("SELECT idUser FROM User where idUser > 1") == null);
			if (id == 1 || module.Database.QueryOne("SELECT idUser FROM User") == null) {
				// This has to be the admin user
				user.AccessLevel = levels.Select().Select(l => l.AsInt("id")).OrderByDescending(l => l).First();
				user.ModulePermissions = false;
				f.Header["AccessLevel"].Type = "select";
				f.Header.Remove("ModulePermissions");
			}
			f.Data = new JObject().AddRange(
				"header", user,
				"detail", permissions(id)
				);
			module.Form = f;
		}

		/// <summary>
		/// List permissions for individual modules
		/// </summary>
		public IEnumerable<Permission> permissions(int user) {
			if (user != 1) {
				Namespace space = module.Server.NamespaceDef;
				foreach (string g in space.AuthGroups.Keys) {
					Permission p = new Permission() {
						UserId = user,
						Module = "-",
						Method = g,
						FunctionAccessLevel = AccessLevel.Any
					};
					if (space.OldAuth && g.Contains(':')) {
						string[] parts = g.Split(':');
						p.Module = parts[0];
						p.Method = parts[1];
					}
					if (user > 1) {
						Permission r = module.Database.Get(p);
						if (r.UserId == user)
							p = r;
					}
					if (space.AuthGroups.TryGetValue(g, out int gid)) {
						p.MinAccessLevel = int.MaxValue;
						foreach (ModuleInfo m in space.Modules) {
							int l = m.LowestAccessLevelForGroup(gid);
							if (l < p.MinAccessLevel)
								p.MinAccessLevel = l;
						}
					}
					yield return p;
				}
			}
		}

		/// <summary>
		/// Update user
		/// </summary>
		public AjaxReturn EditUserSave(JObject json) {
			module.Database.BeginTransaction();
			Table t = module.Database.TableFor("User");
			JObject header = (JObject)json["header"];
			User user = (User)header.To(t.Type);
			bool passwordChanged = false;
			bool firstUser = !module.SecurityOn;
			Utils.Check(header.AsString("Password") + "" == header.AsString("RepeatPassword") + "", "Passwords do not match");
			user.Login = user.Login.Trim();
			user.Email = user.Email.Trim();
			if (user.idUser > 0) {
				// Existing record
				User u = module.Database.Get<User>((int)user.idUser);
				Utils.Check(user.idUser == u.idUser, "Invalid EditUser save");
				if (string.IsNullOrEmpty(user.Password)) {
					user.Password = u.Password;
				} else {
					passwordChanged = true;
				}
			} else {
				passwordChanged = true;
				if (module.Database.QueryOne("SELECT idUser FROM User") == null) {
					user.idUser = 1;        // Admin user
					header["AccessLevel"] = user.AccessLevel = module.Server.NamespaceDef.GetAccessLevel().Select().Select(l => l.AsInt("id")).OrderByDescending(l => l).First();
					header["ModulePermissions"] = user.ModulePermissions = false;
				}
			}
			if (passwordChanged) {
				// New record
				string error = user.PasswordValid(user.Password);
				if (error != null)
					throw new CheckException(error);
				user.UpdatePassword(user.Password);
			}
			AjaxReturn r = module.SaveRecord(user);
			if (!string.IsNullOrEmpty(r.error))
				return r;
			module.Database.Execute("DELETE FROM Permission WHERE UserId = " + user.idUser);
			if (user.idUser > 1 && user.ModulePermissions) {
				t = module.Database.TableFor("Permission");
				foreach (JObject p in ((JArray)json["detail"])) {
					if (p.AsInt("FunctionAccessLevel") >= 0) {
						p["UserId"] = user.idUser;
						module.Database.Insert("Permission", p);
					}
				}
			}
			user.ReloadAccessLevels(module);
			if (firstUser)		// First user just created - log them on
				module.Session.User = user;
			// Update user with new data in all sessions they are logged into
			foreach(var session in module.AllSessionsForUser((int)user.idUser)) {
				session.User = user;
			}
			module.Database.Commit();
			return r;
		}

		/// <summary>
		/// Delete user
		/// </summary>
		public AjaxReturn EditUserDelete(int id) {
			module.Database.BeginTransaction();
			Utils.Check(module.Database.TryGet(id, out User user), "User not found");
			Utils.Check(id > 1 || (id == 1 && module.Database.QueryOne("SELECT idUser FROM User where idUser > 1") == null), 
				"Cannot delete this user");
			module.Database.Execute("DELETE FROM Permission WHERE UserId = " + id);
			module.Database.Execute("DELETE FROM User WHERE iduser = " + id);
			// Log them off from all sessions they are logged into
			foreach (var session in module.AllSessionsForUser(id)) {
				session.User = null;
			}
			module.Database.Commit();
			return new AjaxReturn() { message = "User deleted" };
		}

		/// <summary>
		/// Return the log file for a given date (today if date not supplied)
		/// </summary>
		public void GetLog() {
			string d = module.GetParameters["date"];
			DateTime date = d == null ? Utils.Today : DateTime.Parse(d);
			using (FileStream f = new FileStream(CodeFirstWebFramework.Log.FileName(date), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
				module.WriteResponse(f, "text/plain", System.Net.HttpStatusCode.OK);
			}
		}

		/// <summary>
		/// Create form to change user's password
		/// </summary>
		[Auth(AccessLevel.ReadOnly)]
		public Form ChangePassword() {
			Utils.Check(module.Session.User != null, "You must log in first");
			Form f = new Form(module, true);
			f.Add(new FieldAttribute() {
				Data = "OldPassword",
				Type = "passwordInput",
				MaxLength = 512
			});
			f.Add(new FieldAttribute() {
				Data = "NewPassword",
				Type = "passwordInput",
				MaxLength = 512
			});
			f.Add(new FieldAttribute() {
				Data = "RepeatNewPassword",
				Type = "passwordInput",
				MaxLength = 512
			});
			f["OldPassword"].Options["size"] = 45;
			f["NewPassword"].Options["size"] = 45;
			f["RepeatNewPassword"].Options["size"] = 45;
			f.Data = new JObject();
			return f;
		}

		/// <summary>
		/// Update user's password
		/// </summary>
		[Auth(AccessLevel.ReadOnly, Hide = true)]
		public AjaxReturn ChangePasswordSave(JObject json) {
			User user = module.Session.User;
			Utils.Check(user != null, "You must log in first");
			string oldPassword = json.AsString("OldPassword");
			Utils.Check(user.HashPassword(oldPassword) == user.Password, "Old password does not match");
			string password = json.AsString("NewPassword");
			Utils.Check(oldPassword != password, "New password same as old");
			Utils.Check(password == json.AsString("RepeatNewPassword"), "Passwords do not match");
			string error = user.PasswordValid(password);
			if (error != null)
				throw new CheckException(error);
			user.UpdatePassword(password);
			return module.SaveRecord(user);
		}

		/// <summary>
		/// Display login template, and log user in if form data is posted
		/// </summary>
		[Auth(AccessLevel.Any)]
		public void Login() {
			LoginNoRedirect();
			if(module.Session.User != null)
				RedirectAfterLogin();
		}

		/// <summary>
		/// Logout then show login form
		/// </summary>
		[Auth(AccessLevel.Any)]
		public void Logout() {
			Login();
		}

		/// <summary>
		/// Helper function to implement login
		/// </summary>
		/// <returns>User returned if login or email address is correct, even if password is wrong
		/// to use for (eg) password reset, otherwise null</returns>
		[Auth(int.MaxValue, Hide = true)]	// Not available to web interface
		public User LoginNoRedirect() {
			if (module.Method == "logout")
				module.Session.User = null;
			if (module.Request.HttpMethod == "POST") {
				string login = module.Parameters.AsString("login").Trim();
				string password = module.Parameters.AsString("password");
				module.Message = "Login name not found or password invalid";
				User user = module.Database.QueryOne<User>("SELECT * FROM User WHERE Login = "
					+ module.Database.Quote(login) + " OR Email = " + module.Database.Quote(login));
				if (user.idUser > 0) {
					if (user.HashPassword(password) == user.Password) {
						if(user.OldHashingMethod) {
							user.Password = user.HashPassword(password, true);
							module.Database.Update(user);
						}
						user.ReloadAccessLevels(module);
						module.Session.User = user;
						module.Message = "Logged in successfully";
					}
					return user;
				}
			}
			return null;
		}

		/// <summary>
		/// Helper function to implement redirect after login
		/// </summary>
		[Auth(int.MaxValue, Hide = true)]    // Not available to web interface
		public void RedirectAfterLogin(string redirect = null) {
			if (string.IsNullOrEmpty(redirect)) {
				redirect = module.GetParameters["from"];
				if (string.IsNullOrEmpty(redirect))
					redirect = "/home";
			}
			if (!module.HasAccess(redirect)) {
				foreach (ModuleInfo info in module.Server.NamespaceDef.Modules) {
					if (module.HasAccess("/" + info.Name)) {
						redirect = "/" + info.Name;
						break;
					}
				}
			}
			module.Redirect(redirect);
		}

		static readonly string[] transientItems = new string[] {
			"from", "confirm"
		};

		void removeTransientItemsFromUrl(ref string u) {
			Uri uri = new Uri(u);
			var parms = HttpUtility.ParseQueryString(uri.Query);
			foreach (string t in transientItems)
				parms.Remove(t);
			u = string.Join("?", uri.AbsolutePath, parms.ToString());
		}

		/// <summary>
		/// Save a partially edited record in the CachedRecord table
		/// </summary>
		/// <param name="u">Url of the form edit screen</param>
		/// <param name="h">Hash of the original record</param>
		/// <param name="json">Json of the edited record</param>
		[Auth(AccessLevel.Any)]
		public AjaxReturn SaveCache(string u, long h, string json) {
			AjaxReturn r = new AjaxReturn();
			if (module.Session.User != null) {
				removeTransientItemsFromUrl(ref u);
				if (!module.Database.TryGet(out CachedRecord result, module.Session.User.idUser, u))
					result = new CachedRecord() {
						UserId = (int)module.Session.User.idUser,
						Url = u,
						OriginalRecordHash = h
					};
				result.Json = json;
				module.Database.Update(result);
			}
			return r;
		}

		/// <summary>
		/// Retrieve a partially edited record from the CachedRecord table
		/// </summary>
		/// <param name="u">Url of the form edit screen</param>
		/// <param name="h">Hash of the original record</param>
		[Auth(AccessLevel.Any)]
		public AjaxReturn LoadCache(string u, long h) {
			AjaxReturn r = new AjaxReturn();
			if (module.Session.User != null) {
				removeTransientItemsFromUrl(ref u);
				if (module.Database.TryGet(out CachedRecord result, module.Session.User.idUser, u)) {
					if (result.OriginalRecordHash == h)
						r.data = JObject.Parse(result.Json);
					else
						module.Database.Delete(result);
				}
			}
			return r;
		}

	}


	/// <summary>
	/// Admin module - provides BatchStatus, Backup and Restore. Uses AdminHelper for the implementation.
	/// </summary>
	[Auth(AccessLevel.Admin)]
	[Implementation(typeof(AdminHelper))]
	public class AdminModule : AppModule {
		/// <summary>
		/// Add menu options
		/// </summary>
		protected override void Init() {
			base.Init();
			InsertMenuOptions(
				new MenuOption("Settings", "/admin/editsettings"),
				new MenuOption("Users", "/admin/users"),
				new MenuOption("Batch Jobs", "/admin/batchjobs"),
				new MenuOption("Backup", "/admin/backup"),
				new MenuOption("Restore", "/admin/restore")
				);
			if (SecurityOn) {
				if (Session.User != null)
					InsertMenuOption(new MenuOption("Change password", "/admin/changepassword"));
				InsertMenuOption(new MenuOption(Session.User == null ? "Login" : "Logout", Session.User == null ? "/admin/login" : "/admin/logout"));
			}
		}

		/// <summary>
		/// Display default template
		/// </summary>
		[Auth(AccessLevel.Any)]
		public override void Default() {
		}

	}

	/// <summary>
	/// Table to store partially edited form data, so we can offer to restore it if they come back to the form
	/// </summary>
	[Table]
	public class CachedRecord : JsonObject {
		/// <summary>
		/// Id of user editing the record
		/// </summary>
		[Primary(1, AutoIncrement = false)]
		public int UserId;
		/// <summary>
		/// Url of the form edit screen, with transient variables like from, query, etc. removed
		/// </summary>
		[Primary(2, AutoIncrement = false)]
		public string Url;
		/// <summary>
		/// Hash of the original record
		/// </summary>
		public long OriginalRecordHash;
		/// <summary>
		/// Json of the edited record
		/// </summary>
		[Length(0)]
		public string Json;
	}

}
