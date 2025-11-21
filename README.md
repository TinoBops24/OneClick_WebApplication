N.B please do not commit any changes without first checking with the team.

Follow these steps to get the project running locally:

1. **Copy configuration templates**  
   Duplicate the following template files and rename them:
   - `appsettings.Template.json` → `appsettings.json`  
   - `appsettings.Development.Template.json` → `appsettings.Development.json`

2. **Insert actual settings and secrets**  
   Populate the copied configuration files with the required project values such as:
   - Firebase API keys
   - Database configuration
   - Any other secrets or environment-specific settings

3. **Enable the Secret Manager**
•	Open the Package Manager Console.
•	Run this command:
    "dotnet user-secrets init"
•	This will add a <UserSecretsId> tag to your .csproj file, linking it to a secrets.json file on your machine.

4. **Get Your Firebase Private Key**
•	Go to the Firebase Console and select your project.
•	Click the ⚙️ gear icon and go to Project settings.
•	Go to the Service accounts tab.
•	Click the "Generate new private key" button. A .json file will be downloaded.
   
5. **Store the Key File Securely**
•	Create a folder outside of your project's Git repository to store your keys. For example: C:\FirebaseKeys\ or ~/Documents/FirebaseKeys/.
•	Move the downloaded .json file into this new folder.

6. **Set the Secret**
•	Go back to Package Manager Console.
•	Run the following command, replacing the example path with the actual path to your key file:

**dotnet user-secrets set "Firebase:PrivateKeyFilePath" "C:\FirebaseKeys\your-project-name-firebase-adminsdk.json"**
