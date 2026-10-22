// Cada clase puede levantar PostgreSQL, RabbitMQ o Valkey. Acotar la concurrencia evita que la cantidad
// de CPU del runner dispare demasiados contenedores y hosts de API mientras se recolecta cobertura.
// Se mantiene el aislamiento por clase y se ejecutan todas las pruebas.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
