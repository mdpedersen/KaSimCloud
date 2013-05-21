var processMessageCallback = null;
var proxy = null;

var Simulator = (function () {
    return {
        simulate: function (progStrPar, processMessageCallbackPar) {
            try {
                // record the callback in global.
                processMessageCallback = processMessageCallbackPar;               

                // load signalR scripts.
                $.getScript("{BASEURL}/Scripts/jquery.signalR-1.0.1.js")                
                .fail(function (jqxhr, settings, exception) {
                    alert("Failed to load signalR: " + exception);
                })
                .done(function (script, textStatus) {
                    
                    // get signalR proxy.
                    var connection = $.hubConnection("{BASEURL}");                    
                    proxy = connection.createHubProxy('kaSimHub');

                    // set signalR simulation update handler - basically just passes message on to simulate callback.
                    proxy.on("simulationUpdate", function (time, newSpec, newData, msg, isComplete) {    
                        var message = { time: time, isComplete: isComplete, msg: msg, newSpec: newSpec, newData: newData };
                        processMessageCallback(message);
                    });                    

                    // start connection.
                    connection.start()
                    .done(function () {
                        
                        // extract simulation parameters from program and comment out:
                        var regex = /^%simulate: (.+)/m;
                        var matches = regex.exec(progStrPar);
                        var customArgs = "";
                        if (matches[1] != null) {
                            customArgs = matches[1];
                        } 
                        progStrPar = progStrPar.replace(regex, "# " + matches[0]);

                        // call simulation start method in hub.
                        proxy.invoke("RunKappa", progStrPar, customArgs)
                        .done(function (x) { });
                        
                    })
                    .fail(function (e1) {
                        var message = { isComplete: true, msg: "Simulation failed: " + e1 };
                        processMessageCallback(message);
                    });
                });
            }
            catch (e) {                
                var message = { isComplete: true, msg: "Simulation failed: " + e };
                processMessageCallback(message);
            }
        },

        stop: function () {            
            // ensure that the KaSim process is killed on the server.
            proxy.invoke("StopSimulation");
        },
    };

})();

