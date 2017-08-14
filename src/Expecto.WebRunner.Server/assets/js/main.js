$(function () {
    let _self = this;
    let $main = $('#expecto-webrunner');
    let $alert = $('#expecto-webrunner-alert');
    let $runAll = $('#run-all');

    $.get("discover", function(testSet) {
        $main.html(renderTestSet(testSet));
        $('.test .collapse').collapse();
        _self.testIndex = indexTests();
        $('.test .run').unbind('click').click(function () {
            alert('click!');
        });
    });

    $runAll.click(function () {
        $('.test .status')
            .text('pending')
            .removeClass('hidden')
            .addClass('badge-info');
        doSend({ commandName: "run all" });
    })

    initCommandChannel('ws://localhost:8082/command');

    //$alert.alert();

    function indexTests() {
        var map = {};
        $('.test').each(function () {
            var $this = $(this);
            var testCode = $this.attr('data-test-name');
            map[testCode] = {
                $status: $this.find('.status'),
                $message: $this.find('.message')
            };
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
        let collapseId = ['collapse-',testCode].join('');
        return [
            '<div class="test card" data-test-name="',escapeTestName(testCode),'">',
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