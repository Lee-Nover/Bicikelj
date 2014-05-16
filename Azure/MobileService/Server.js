var port = process.env.port || 1337;
var express = require('express');
var app = module.exports = express();
app.use(express.compress());
app.use(express.bodyParser());
process.env.EMULATED = true;

app.get('/', function (req, resp) {
    resp.type('text/plain'); // set content-type
    resp.send('use the path as:  server/serviceName/city/{stationId} where stationId is optional.'); // send text response
});

app.get('/versions/:version?', function (req, resp) {
    var api = require('./api/versions');
    try {
        api.get(req, resp);
    } catch (ex) {
        console.error(ex);
        resp.send(500, ex);
   }
});

app.post('/versions/:version', function (req, resp) {
    var api = require('./api/versions');
    try {
        api.post(req, resp);
    } catch (ex) {
        console.error(ex);
        resp.send(500, ex);
   }
});

app.get('/:service/:city/:id?', function (req, resp) {
    var api = require('./api/stations');
    try {
        api.get(req, resp);
    } catch (ex) {
        console.error(ex);
        resp.send(500, ex);
   }
});


process.on('uncaughtException', function (err) {
  console.error(err);
  //response.send(500, err);
});

app.listen(port);