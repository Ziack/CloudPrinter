# GoogleCloudPrint C&#35; Gateway

## Usage

1. Create a service API account at https://console.developers.google.com:
  Go to "Credentials", click "Create credentials" and select "Service account key".
2. Generate a P12 key based on the service account you just made and export it. Place this file in your project folder and ensure the file properties are set to "Content" and "Copy if newer".
3. Go to https://console.developers.google.com/iam-admin/serviceaccounts/ and save the "Service account ID" (email address) that was generated for your service account. You need to share your printer with this email address in order for the service account to be able to print to your printer.
4. Go to https://www.google.com/cloudprint/#printers and click "Details" for your printer.  Save the guid you see under "Advanced Details" > "Printer ID" and Access Token.
5. Now let's test if it works. Use the following syntax to connect to cloud print (The "source" parameter is just a name for your application, for example "io7-googlecloudprint"):

    ```csharp
    var service = new GoogleCloudPrintService(accessToken: "{access_token_provided_by_server}");

    var printerCollection = service.GetPrinters();
    
    foreach (var printer in printerCollection.printers)
    {
        Console.WriteLine(printer.name);
    }
    ```
