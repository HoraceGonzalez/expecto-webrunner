$(function () {
    let _self = this;
    let $main = $('#expecto-webrunner');
    let $alert = $('#expecto-webrunner-alert');
    let $runAll = $('#run-all');

    $.get("discover", function(testSet) {
        $main.html(renderTestSet(testSet));
        $('.test .collapse').collapse();
        _self.testIndex = indexTests();
        
    });

    $runAll.click(function () {
        $('.test .status')
            .text('pending')
            .removeClass('hidden')
            .addClass('badge-info');
        doSend({ commandName: "run all" });
    })

    initCommandChannel('ws://localhost:8082/command');

    function indexTests() {
        var map = {};
        $('.test').each(function () {
            var $this = $(this);
            var testCode = $this.attr('data-test-name');
            var assemblyPath = $this.attr('data-assembly-path');
            
            map[testCode] = {
                $status: $this.find('.status'),
                $message: $this.find('.message'),
                $run: $this.find('.run')
            };
            
            map[testCode].$run.unbind('click').click(function () {
                doSend({ 
                    commandName: 'run test',
                    testCode: testCode,
                    assemblyPath: assemblyPath
                 });
            });
        });
        return map;
    }

    function renderTestSet(testSet) {
        return [
            $.map(testSet,renderTestList).join('\n')
        ].join('\n');
    }

    function renderTestList(testList) {
        return [
            '<h3 class="py-3">',testList.assemblyName,'</h3>',
            $.map(testList.testCases,renderTestCase).join('\n')
        ].join('\n');
    }
        
    function escapeTestName(testName) {
        return testName.replace('"', '\"');
    }

    function renderTestCase(testCase) {
        let testCode = testCase.testCode.replace(/[\W]/gi,'-');
        let headingId = ['heading-',testCode].join('');
        let assemblyPath = testCase.assemblyPath.replace('"','\"');
        let collapseId = ['collapse-',testCode].join('');
        return [
            '<div class="test card" data-test-name="',escapeTestName(testCode),' data-assembly-path="',assemblyPath,'">',
                '<div class="card-header" role="tab" id="',headingId,'">',
                    '<h5 class="mb-0">',
                        '<a class="run btn btn-sm btn-primary mr-3" href="#" role="button">Run</a>',
                        '<span class="status badge mr-3 hidden"></span>',
                        '<a class="collapsed" data-toggle="collapse" data-parent="#expecto-webrunner" href="#',collapseId,'" aria-expanded="false" aria-controls="',collapseId,'">',
                            '<small><code>',testCase.testCode,'</code></small>',
                        '</a>',
                    '</h5>',
                '</div>',
                '<div id="',collapseId,'" class="collapse show" role="tabpanel" aria-labelledby="',headingId,'" data-parent="#accordion">',
                    '<div class="message card-body"></div>',
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
        //var message = JSON.parse(evt.data);
        var message = JSON.parse("{
            \"updateName\": \"TestPassed\",
            \"data\": {
                \"name\": \"Authorization/Implementation/No other permission except All should be loose\",
                \"duration\": \"00:00:00.0120000\"
            }

        }");
        var updateName = message.updateName || '';
        
        console.log('RESPONSE: ' + message);
        if (updateName === 'TestPassed') {
            var testCode = message.data.name;
            var test = _self.testIndex[testCode];
            console.log('test ' + test)
            test.$status
                .text('PASSED')
                .addClass('badge-success')
                .removeClass('badge-danger')
                .removeClass('badge-info');
        } else if (updateName === 'TestFailed') {
            var testCode = message.data.testCode;
            var test = _self.testIndex[testCode];
            test.$status
                .text('FAILED')
                .addClass('badge-danger')
                .removeClass('badge-success')
                .removeClass('badge-info');
        } else if (updateName === 'TestStarting') {
            var testCode = message.data.testCode;
            var test = _self.testIndex[testCode];
            test.$status
                .text('Running')
                .addClass('badge-info')
                .removeClass('badge-danger')
                .removeClass('badge-success');
        } 
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