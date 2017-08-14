$(function () {

    let $main = $('#expecto-webrunner');
    let $alert = $('#expecto-webrunner-alert');
    let $runAll = $('#run-all');

    $.get("discover", function(testSet) {
        $main.html(renderTestSet(testSet));
    });

    $runAll.click(function () {
        doSend({ commandName: "run all" });
    })

    initCommandChannel('ws://localhost:8082/command');

    //$alert.alert();

    function renderTestSet(testSet) {
        return [
            $.map(testSet,renderTestList).join('\n')
        ].join('\n');
    }

    function renderTestList(testList) {
        return [
            '<h3>',testList.assemblyName,'</h3>',
            $.map(testList.testCases,renderTestCase).join('\n')
        ].join('\n');
    }
        
    function renderTestCase(testCase) {
        let testCode = testCase.testCode.replace(/[^A-z]/gi,'-');
        let headingId = ['heading-',testCode].join('');
        let collapseId = ['collapse-',testCode].join('');
        return [
            '<div class="card">',
                '<div class="card-header" role="tab" id="',headingId,'">',
                    '<h5 class="mb-0">',
                        '<a class="btn btn-primary" href="#" role="button">Run</a>',
                        '<a data-toggle="collapse" href="#',collapseId,'" aria-expanded="true" aria-controls="',collapseId,'">',
                            '<code>',testCase.testCode,'</code>',
                        '</a>',
                        '<span class="badge badge-success">PASS</span>',
                    '</h5>',
                '</div>',
                '<div id="',collapseId,'" class="collapse show" role="tabpanel" aria-labelledby="',headingId,'" data-parent="#accordion">',
                    '<div class="card-body">',
                        'Anim pariatur cliche reprehenderit, enim eiusmod high life accusamus terry richardson ad squid',
                    '</div>',
                '</div>',
            '</div>'
        ].join('');
    }

    function initCommandChannel(wsUri) {
        websocket = new WebSocket(wsUri);
        websocket.onopen = function(evt) { onOpen(evt) };
        websocket.onclose = function(evt) { onClose(evt) };
        websocket.onmessage = function(evt) { onMessage(evt) };
        websocket.onerror = function(evt) { onError(evt) };
    }
  
    function onOpen(evt) {
        console.log("CONNECTED");
    }
  
    function onClose(evt) {
        console.log("DISCONNECTED");
    }
  
    function onMessage(evt) {
        console.log('RESPONSE: ' + evt.data);
    }

    function onError(evt) {
        console.error('ERROR: ' + evt.data);    
    }
  
    function doSend(message) {
        let json = JSON.stringify(message,null,'\t');
        console.log ('SENT: ' + json);
        websocket.send(json);
    }
  
    function writeToScreen(message) {
        var pre = document.createElement("p");
        pre.style.wordWrap = "break-word";
        pre.innerHTML = message;
        output.appendChild(pre);
    }
})