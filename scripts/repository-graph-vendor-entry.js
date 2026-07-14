import Graph from "graphology";
import Sigma from "sigma";
import { EdgeArrowProgram } from "sigma/rendering";
import forceAtlas2 from "graphology-layout-forceatlas2";
import FA2Layout from "graphology-layout-forceatlas2/worker";

window.SvnHubGraphVendor = {
  Graph,
  Sigma,
  EdgeArrowProgram,
  forceAtlas2,
  FA2Layout,
};
