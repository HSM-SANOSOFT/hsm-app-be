nest g mo core modules -p main

nest g mo coms modules/core -p main
nest g mo docs modules/core -p main

nest g co coms modules/core -p main
nest g co docs modules/core -p main

nest g s coms modules/core -p main
nest g s docs modules/core -p main

nest g mo security modules -p main

nest g mo auth modules/security -p main
nest g mo user modules/security -p main
nest g mo roles modules/security -p main

nest g co auth modules/security -p main
nest g co user modules/security -p main
nest g co roles modules/security -p main

nest g s auth modules/security -p main
nest g s user modules/security -p main
nest g s roles modules/security -p main

nest g mo clinical modules -p main

nest g mo patients modules/clinical -p main
nest g mo appointments modules/clinical -p main

nest g co patients modules/clinical -p main
nest g co appointments modules/clinical -p main

nest g s patients modules/clinical -p main
nest g s appointments modules/clinical -p main

nest g mo administrative modules -p main

nest g mo scheduling modules/administrative -p main

nest g co scheduling modules/administrative -p main

nest g s scheduling modules/administrative -p main