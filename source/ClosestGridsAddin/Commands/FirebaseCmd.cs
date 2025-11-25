using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Models;
using FireSharp.Config;
using FireSharp.Interfaces;
using FireSharp.Response;
using Newtonsoft.Json;
using Nice3point.Revit.Toolkit.External;
using System.IO;

namespace ClosestGridsAddinVANILLA.Commands
{

    [Transaction(TransactionMode.Manual)]
    public class FirebaseCmd : ExternalCommand
    {
        public IFirebaseConfig fc = new FirebaseConfig()
        {
            AuthSecret = "I2yypO4zT4LNHCG9NrBwI9VebMdOn9f4PiZwjlTY",
            BasePath = "https://bims-8d507-default-rtdb.firebaseio.com/"
        };

        public IFirebaseClient client;
        public override void Execute()
        {
            try
            {
                

                

            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        


        
    }
}

