$(function () {
    let _self = this;
    let $main = $('#expecto-webrunner');
    let $alert = $('#expecto-webrunner-alert');
    let $runAll = $('#run-all');

    // start initiailize
    $runAll.click(function () {
        $('#expecto-webrunner .test .collapse').collapse();
        $('#expecto-webrunner .test .status')
            .text('pending')
            .removeClass('hidden')
            .removeClass('badge-success')
            .removeClass('badge-danger')
            .addClass('badge-info');
        doSend({ commandName: "run all" });
    });

    initCommandChannel('ws://localhost:8082/command');
    // end initialize

    function loadTestSet(testSet) {
        $main.html(renderTestSet(testSet));
        $('#expecto-webrunner .test .collapse').collapse();
        _self.testIndex = indexTests();   
    }

    function indexTests() {
        var map = {};
        $('#expecto-webrunner .test').each(function () {
            var $this = $(this);
            var testCode = $this.attr('data-test-name');
            var assemblyPath = $this.attr('data-assembly-path');
            
            map[testCode] = {
                $status: $this.find('.status'),
                $message: $this.find('.message'),
                collapse: function(status) {
                    $this.find('.message-dropdown').collapse(status)
                },
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
            '<div class="test card" data-test-name="',escapeTestName(testCase.testCode),'" data-assembly-path="',assemblyPath,'">',
                '<div class="card-header" role="tab" id="',headingId,'">',
                    '<h5 class="mb-0">',
                        '<button class="run btn btn-sm btn-primary mr-3" href="#" type="button">Run</button>',
                        '<span class="status badge mr-3 hidden"></span>',
                        '<a class="collapsed" data-toggle="collapse" data-parent="#expecto-webrunner" href="#',collapseId,'" aria-expanded="false" aria-controls="',collapseId,'">',
                            '<small><code>',testCase.testCode,'</code></small>',
                        '</a>',
                    '</h5>',
                '</div>',
                '<div id="',collapseId,'" class="message-dropdown collapse show" role="tabpanel" aria-labelledby="',headingId,'" data-parent="#accordion">',
                    '<div class="message card-body"></div>',
                '</div>',
            '</div>'
        ].join('');
    }

    function updateTestUi(testCode, status, description, duration) {
        var test = _self.testIndex[testCode];
        test.$status
            .text(status)
            .removeClass('badge-success')
            .removeClass('badge-danger')
            .removeClass('badge-info');
        switch (status.toLowerCase()) {
            case 'passed': test.$status.addClass('badge-success'); break;
            case 'failed': test.$status.addClass('badge-danger'); break;
            case 'pending': test.$status.addClass('badge-info'); break;
            default: test.$status.addClass('badge-info'); break;
        }
    
        var durationMarkup = duration != null
            ? ['<p><pre>Duration: ',duration,'</pre></p>'].join('')
            : '';

        var descriptionMarkup = description != null
            ? ['<p><pre>',description,'</pre></p>'].join('')
            : '';

        test.$message.html([
            durationMarkup,
            descriptionMarkup
        ].join(''));

        switch (status.toLowerCase()) {
            case 'failed': test.collapse('show'); break;
            default: test.collapse(); break;
        }
    }

    function alertAppStatus(message) {
        var pre = document.createElement("p");
        pre.style.wordWrap = "break-word";
        pre.innerHTML = message;
        output.appendChild(pre);
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
        doSend({ commandName: 'discover all' });
    }
  
    function onClose(evt) {
        console.log("DISCONNECTED");
    }

    function onMessage(evt) {
        var message = JSON.parse(evt.data);
        var updateName = message.updateName || '';
        console.log('RESPONSE: ')
        console.log(message);

        switch (updateName.toLowerCase()) {
            case 'testsetdiscovered':
                loadTestSet(message.data);
                break;
            case 'testpassed':
                updateTestUi(message.data.name, 'PASSED', null, message.data.duration); 
                break;
            case 'testfailed': 
                updateTestUi(message.data.name, 'FAILED', message.data.message, message.data.duration); 
                break;
            case 'testignored': 
                updateTestUi(message.data.name, 'Ignored', message.data.message); 
                break;
            case 'teststarting': 
                updateTestUi(message.data.name, 'Pending'); 
                break;
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
})